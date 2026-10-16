using System.Security.Cryptography;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class HandoverDownloadService : IHandoverDownloadService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IAssetContentStore contentStore;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverDownloadService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IHandoverRepository handover,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.handover = handover;
        this.contentStore = contentStore;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<PreparedDownload> PrepareAsync(Guid grantId, Guid itemId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();

        // Grant lạ và grant của người khác cho cùng một lỗi, không ghi nhật ký (SEC-04).
        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        if (grant == null || grant.UserId != userId)
        {
            throw HandoverNotFound();
        }

        HandoverTurn turn = grant.Turn;
        HandoverSnapshotItem? item = turn.Items.FirstOrDefault(i => i.Id == itemId);
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (item == null || vault == null)
        {
            throw HandoverNotFound();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? deniedReason = HandoverRules.GrantDeniedReason(grant, vault.Plan.Status, now);
        if (deniedReason != null)
        {
            await WriteLogAsync(grant, item, userId, HandoverAccessOutcome.Denied, now, ct);

            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = deniedReason;
            throw new AppException(AppErrorType.Forbidden, ErrorCode.GrantNotActive,
                "Quyền tải tài sản không còn hiệu lực.", extensions);
        }

        AssetVersion? version = await handover.FindAssetVersionAsync(item.AssetVersionId, ct);
        if (version == null)
        {
            await WriteLogAsync(grant, item, userId, HandoverAccessOutcome.IntegrityFailed, now, ct);
            throw new ContentIntegrityException("Không tìm thấy phiên bản nội dung của tài sản.", null);
        }
        if (version.ContentDeletedAt != null)
        {
            // RETENTION-06: nội dung đã dọn (khóa đã hủy) thì không giải mã nữa.
            Dictionary<string, object?> deleted = new Dictionary<string, object?>();
            deleted["reason"] = "CONTENT_DELETED";
            throw new AppException(AppErrorType.Forbidden, ErrorCode.GrantNotActive,
                "Nội dung đã được xóa theo chính sách lưu dữ liệu.", deleted);
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = version.StorageKey;
        stored.SizeBytes = version.SizeBytes;
        stored.Sha256 = version.Sha256;
        stored.EncryptionKeyId = version.EncryptionKeyId;
        stored.WrappedDataKey = version.WrappedDataKey;

        // Lượt 1: giải mã vào Stream.Null. Việc kiểm kích thước + SHA-256 của kho chỉ xảy ra ở CUỐI luồng,
        // nên phải chạy hết một lượt trước khi gửi byte đầu tiên, nếu không người nhận có thể nhận tệp hỏng.
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            await WriteLogAsync(grant, item, userId, HandoverAccessOutcome.IntegrityFailed, now, ct);
            throw new ContentIntegrityException("Nội dung tài sản không vượt qua kiểm tra toàn vẹn.", ex);
        }

        await WriteLogAsync(grant, item, userId, HandoverAccessOutcome.Succeeded, now, ct);

        PreparedDownload download = new PreparedDownload();
        download.FileName = ResolveFileName(item);
        download.ContentType = item.ContentType;
        download.Length = item.SizeBytes;
        // Lượt 2: giải mã lần nữa, lần này ghi thẳng ra response.
        download.WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token);
        return download;
    }

    /// <summary>Nhật ký chỉ ghi id và kết quả, không ghi tên tệp hay nội dung (OPS-04).</summary>
    private async Task WriteLogAsync(
        HandoverGrant grant, HandoverSnapshotItem item, Guid userId,
        HandoverAccessOutcome outcome, DateTimeOffset now, CancellationToken ct)
    {
        HandoverAccessLog log = new HandoverAccessLog();
        log.GrantId = grant.Id;
        log.SnapshotItemId = item.Id;
        log.UserId = userId;
        log.Outcome = outcome;
        log.At = now;
        handover.AddAccessLog(log);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static string ResolveFileName(HandoverSnapshotItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.FileName))
        {
            return item.FileName;
        }

        if (item.Kind == AssetKind.Text)
        {
            return item.Name + ".txt";
        }

        return item.Name;
    }

    private static AppException HandoverNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound,
            "Không tìm thấy nội dung bàn giao.");
    }
}
