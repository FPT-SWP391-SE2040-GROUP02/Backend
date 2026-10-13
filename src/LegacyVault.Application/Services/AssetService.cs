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
/// Giới hạn đã biết: hai lần tải lên đồng thời cùng một kho có thể cùng qua bước kiểm tra quota
/// (cần khóa theo kho khi làm phần thanh toán/quota đầy đủ).
/// </remarks>
public sealed partial class AssetService : IAssetService
{
    private const int MaxNameLength = 200;
    private const int MaxFileNameLength = 255;
    private const int MaxContentTypeLength = 100;
    private const string DefaultContentType = "application/octet-stream";
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
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public AssetService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IAssetRepository assetRepository,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.assetRepository = assetRepository;
        this.contentStore = contentStore;
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
        long usedBytes = await vaultRepository.SumUsedBytesAsync(vault.Id, ct);
        long remainingBytes = Math.Max(0, quotaBytes - usedBytes);
        if (declaredLength > remainingBytes)
        {
            throw QuotaExceeded(quotaBytes, remainingBytes);
        }

        // Kích thước khai báo có thể sai: kho lưu trữ tự đếm byte thật và dừng khi vượt phần còn lại.
        // Kho lưu trữ mã hóa nội dung trước khi ghi; ở đây chỉ nhận lại khóa đã bọc để lưu vào DB.
        StoredContent stored;
        try
        {
            stored = await contentStore.SaveAsync(content, remainingBytes, ct);
        }
        catch (AppException ex) when (ex.Code == ErrorCode.FileTooLarge)
        {
            throw QuotaExceeded(quotaBytes, remainingBytes);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Asset asset = new Asset();
        asset.VaultId = vault.Id;
        asset.Kind = kind;
        asset.Name = name;
        asset.Description = TextInput.NullIfBlank(description);
        asset.CurrentVersionNumber = 1;
        asset.CreatedAt = now;

        AssetVersion version = new AssetVersion();
        version.AssetId = asset.Id;
        version.VersionNumber = 1;
        version.FileName = fileName;
        version.ContentType = contentType;
        version.SizeBytes = stored.SizeBytes;
        version.Sha256 = stored.Sha256;
        version.StorageKey = stored.StorageKey;
        version.EncryptionKeyId = stored.EncryptionKeyId;
        version.WrappedDataKey = stored.WrappedDataKey;
        version.CreatedAt = now;
        asset.Versions.Add(version);

        assetRepository.Add(asset);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
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
