using System.Security.Cryptography;
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

public interface IExecutorAssetService
{
    /// <summary>Danh sách tài sản (chỉ thông tin mô tả) của kho mà tài khoản đang là Người thực thi hiệu lực.</summary>
    Task<PaginatedList<AssetResponse>> ListAsync(Guid vaultId, PaginationRequest pagination, CancellationToken ct);

    /// <summary>
    /// Tải nội dung một tài sản của kho đang phụ trách. Server giải mã như lúc chủ kho tải, kiểm toàn vẹn trước byte đầu tiên
    /// và ghi nhật ký người thực hiện là Executor. Không phải Executor hiệu lực của kho → 404.
    /// </summary>
    Task<PreparedDownload> PrepareContentAsync(Guid vaultId, Guid assetId, CancellationToken ct);
}

/// <remarks>
/// - Chỉ Executor hiệu lực (đã chấp nhận, chưa bị thay) của đúng kho; chủ kho và người lạ nhận 404 như kho không tồn tại.
/// - Mỗi lần tải nội dung ghi một dòng nhật ký (AssetContentDownloaded, vai trò Executor); xem danh sách không ghi.
/// - Tài sản đã bị xóa nội dung theo chính sách lưu giữ thì 404.
/// </remarks>
public sealed class ExecutorAssetService : IExecutorAssetService
{
    private static readonly Dictionary<string, string> SortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Name",
            ["createdAt"] = "CreatedAt",
        };

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorReader executorReader;
    private readonly IAssetRepository assetRepository;
    private readonly IAssetContentStore contentStore;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ExecutorAssetService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorReader executorReader,
        IAssetRepository assetRepository,
        IAssetContentStore contentStore,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.executorReader = executorReader;
        this.assetRepository = assetRepository;
        this.contentStore = contentStore;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<PaginatedList<AssetResponse>> ListAsync(Guid vaultId, PaginationRequest pagination, CancellationToken ct)
    {
        await RequireExecutorAsync(vaultId, ct);

        string sortBy = "CreatedAt";
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

    public async Task<PreparedDownload> PrepareContentAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        Guid userId = await RequireExecutorAsync(vaultId, ct);
        Asset? asset = await assetRepository.FindByIdAsync(vaultId, assetId, ct);
        if (asset == null || asset.ContentDeletedAt != null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound, "Không tìm thấy tài sản.");
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = asset.StorageKey;
        stored.SizeBytes = asset.SizeBytes;
        stored.EncryptionKeyId = asset.EncryptionKeyId;
        stored.WrappedDataKey = asset.WrappedDataKey;

        // Lượt 1 giải mã vào Stream.Null để kiểm toàn vẹn trước khi gửi byte đầu tiên, như lúc chủ kho tải.
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Nội dung tài sản không vượt qua kiểm tra toàn vẹn.", ex);
        }

        auditLogRepository.Add(AuditRules.Create(
            vaultId, userId, AuditActorRole.Executor, AuditAction.AssetContentDownloaded, AuditTargetType.Asset,
            asset.Id, null, AuditOutcome.Succeeded, null, timeProvider.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);

        PreparedDownload download = new PreparedDownload();
        download.FileName = !string.IsNullOrWhiteSpace(asset.FileName)
            ? asset.FileName
            : (asset.Kind == AssetKind.Text ? asset.Name + ".txt" : asset.Name);
        download.ContentType = asset.ContentType;
        download.Length = asset.SizeBytes;
        download.WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token);
        return download;
    }

    private async Task<Guid> RequireExecutorAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault != null && vault.OwnerId != userId)
        {
            List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
            foreach (Executor executor in executors)
            {
                if (executor.UserId == userId)
                {
                    return userId;
                }
            }
        }
        throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
    }
}
