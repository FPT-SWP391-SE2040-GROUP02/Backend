using System.Text;
using System.Text.RegularExpressions;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// Thứ tự tạo: kiểm tra quyền/trạng thái/quota → mã hóa và ghi nội dung vào kho lưu trữ → INSERT DB.
/// INSERT lỗi thì xóa object vừa ghi để không để lại nội dung mồ côi.
/// Quota: mỗi lần tải giữ chỗ 15 phút dưới khóa riêng của kho (IStorageReservationRepository), nên hai lần tải
/// đồng thời không cùng vượt hạn mức (QUOTA-02).
/// </remarks>
public sealed partial class AssetService : IAssetService
{
    private const int MaxNameLength = 200;
    private const int MaxFileNameLength = 255;
    private const int MaxContentTypeLength = 100;
    private const string DefaultContentType = "application/octet-stream";
    /// <summary>QUOTA-02: giữ chỗ dung lượng 15 phút cho mỗi lần tải lên.</summary>
    private static readonly TimeSpan ReservationHold = TimeSpan.FromMinutes(15);

    private const string TextContentType = "text/plain; charset=utf-8";
    private const string DefaultSortBy = "createdAt";

    private static readonly Dictionary<string, string> SortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["createdAt"] = "createdAt",
        ["name"] = "name",
    };

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IAssetRepository assetRepository;
    private readonly IAssetContentStore contentStore;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IStorageReservationRepository reservations;
    private readonly IContainerRepository containerRepository;
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public AssetService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IAssetRepository assetRepository,
        IAssetContentStore contentStore,
        IAuditLogRepository auditLogRepository,
        IStorageReservationRepository reservations,
        IContainerRepository containerRepository,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.assetRepository = assetRepository;
        this.contentStore = contentStore;
        this.auditLogRepository = auditLogRepository;
        this.reservations = reservations;
        this.containerRepository = containerRepository;
        this.holdReader = holdReader;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<AssetResponse> CreateFileAssetAsync(
        Guid vaultId, CreateFileAssetRequest request, CancellationToken ct)
    {
        string? fileName = SanitizeFileName(request.FileName);
        if (fileName == null)
        {
            throw ValidationErrors.For("file", "Vui lòng chọn tệp.");
        }
        if (request.Length <= 0)
        {
            throw ValidationErrors.For("file", "Tệp rỗng.");
        }

        // Không gửi tên thì lấy theo tên tệp.
        string? name = TextInput.NullIfBlank(request.Name);
        if (name == null)
        {
            name = Truncate(fileName, MaxNameLength);
        }

        return await CreateAsync(vaultId, AssetKind.File, name, request.Description, fileName,
            SanitizeContentType(request.ContentType), request.Length, request.Content, ct);
    }

    public async Task<AssetResponse> CreateTextAssetAsync(
        Guid vaultId, CreateTextAssetRequest request, CancellationToken ct)
    {
        string? name = TextInput.NullIfBlank(request.Name);
        if (name == null)
        {
            throw ValidationErrors.For("name", "Tên tài sản là bắt buộc.");
        }
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw ValidationErrors.For("content", "Nội dung là bắt buộc.");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(request.Content);
        using (MemoryStream content = new MemoryStream(bytes, writable: false))
        {
            return await CreateAsync(vaultId, AssetKind.Text, name, request.Description, null,
                TextContentType, bytes.LongLength, content, ct);
        }
    }

    public async Task<PaginatedList<AssetResponse>> GetAssetsAsync(
        Guid vaultId, PaginationRequest pagination, CancellationToken ct)
    {
        await LoadOwnedVaultAsync(vaultId, ct);

        string sortBy = DefaultSortBy;
        if (!string.IsNullOrWhiteSpace(pagination.SortBy))
        {
            string? column;
            if (!SortColumns.TryGetValue(pagination.SortBy, out column))
            {
                throw ValidationErrors.For("sortBy", "Chỉ sắp xếp theo name hoặc createdAt.");
            }
            sortBy = column;
        }

        var page = await assetRepository.FindPageByVaultIdAsync(
            vaultId, pagination.PageIndex, pagination.PageSize, TextInput.NullIfBlank(pagination.SearchTerm),
            sortBy, pagination.IsAscending, ct);

        List<AssetResponse> responses = new List<AssetResponse>();
        foreach (Asset asset in page.Items)
        {
            responses.Add(asset.ToResponse());
        }
        return responses.ToPaginatedList(page.TotalCount, pagination);
    }

    public async Task<AssetResponse> GetAssetAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        await LoadOwnedVaultAsync(vaultId, ct);
        Asset? asset = await assetRepository.FindByIdAsync(vaultId, assetId, ct);
        if (asset == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound, "Không tìm thấy tài sản.");
        }
        return asset.ToResponse();
    }

    private async Task<AssetResponse> CreateAsync(
        Guid vaultId, AssetKind kind, string name, string? description, string? fileName,
        string contentType, long declaredLength, Stream content, CancellationToken ct)
    {
        if (name.Length > MaxNameLength)
        {
            throw ValidationErrors.For("name", "Tên tài sản tối đa 200 ký tự.");
        }

        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultRules.EnsureEditable(vault);

        long quotaBytes = vault.Entitlement.StorageQuotaBytes;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // QUOTA-02: giữ chỗ 15 phút, tính cả phần các lần tải khác đang giữ. Kiểm và ghi chỗ giữ dưới khóa của kho.
        ReservationOutcome outcome = new ReservationOutcome();
        StorageReservation? reservation = await reservations.TryReserveAsync(
            vault.Id, declaredLength, quotaBytes, now, ReservationHold, outcome, ct);
        if (reservation == null)
        {
            throw QuotaExceeded(quotaBytes, outcome.RemainingBytes);
        }

        // Kích thước khai báo có thể sai: kho lưu trữ tự đếm byte thật và dừng khi vượt phần đã giữ chỗ.
        // Kho lưu trữ mã hóa nội dung trước khi ghi; ở đây chỉ nhận lại khóa đã bọc để lưu vào DB.
        StoredContent stored;
        try
        {
            stored = await contentStore.SaveAsync(content, declaredLength, ct);
        }
        catch (AppException ex) when (ex.Code == ErrorCode.FileTooLarge)
        {
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            throw QuotaExceeded(quotaBytes, outcome.RemainingBytes);
        }
        catch
        {
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            throw;
        }

        Asset asset = new Asset();
        asset.VaultId = vault.Id;
        asset.Kind = kind;
        asset.Name = name;
        asset.Description = TextInput.NullIfBlank(description);
        asset.FileName = fileName;
        asset.ContentType = contentType;
        asset.SizeBytes = stored.SizeBytes;
        asset.StorageKey = stored.StorageKey;
        asset.EncryptionKeyId = stored.EncryptionKeyId;
        asset.WrappedDataKey = stored.WrappedDataKey;
        asset.CreatedAt = now;

        assetRepository.Add(asset);
        // SEC-13: lưu cùng tài sản; INSERT lỗi thì nhật ký cũng không ghi. Không ghi tên tài sản hay tên tệp (SEC-07).
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, AuditAction.AssetCreated, AuditTargetType.Asset, asset.Id,
            null, now));
        // Chỗ giữ chuyển sang đã dùng trong cùng lần lưu với tài sản.
        reservations.MarkCommitted(reservation, now);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            // Bù trừ: không để nội dung không có bản ghi trong kho lưu trữ. Không dùng ct vì request có thể đã bị hủy.
            await contentStore.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        return asset.ToResponse();
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    private static AppException QuotaExceeded(long quotaBytes, long remainingBytes)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["storageQuotaBytes"] = quotaBytes;
        extensions["storageRemainingBytes"] = remainingBytes;
        return new AppException(AppErrorType.Conflict, ErrorCode.StorageQuotaExceeded,
            "Kho không còn đủ dung lượng cho tài sản này.", extensions);
    }

    /// <summary>Chỉ giữ tên tệp (bỏ đường dẫn), bỏ ký tự điều khiển, tối đa 255 ký tự. Rỗng → null.</summary>
    private static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string[] parts = fileName.Replace('\\', '/').Split('/');
        string baseName = parts[parts.Length - 1];

        StringBuilder cleaned = new StringBuilder();
        foreach (char c in baseName)
        {
            if (!char.IsControl(c))
            {
                cleaned.Append(c);
            }
        }

        string result = cleaned.ToString().Trim();
        if (result.Length == 0)
        {
            return null;
        }
        return Truncate(result, MaxFileNameLength);
    }

    /// <summary>Chỉ nhận dạng "type/subtype" hợp lệ (bỏ phần ";charset=..."); còn lại dùng application/octet-stream.</summary>
    private static string SanitizeContentType(string? contentType)
    {
        if (contentType == null)
        {
            return DefaultContentType;
        }

        string value = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (value.Length > MaxContentTypeLength || !MimeTypePattern().IsMatch(value))
        {
            return DefaultContentType;
        }
        return value;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }
        return value.Substring(0, maxLength);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$")]
    private static partial Regex MimeTypePattern();
}
