using System.Security.Cryptography;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

// Người nhận bấm "Tải" một tài sản đã được bàn giao (PrepareAsync).
// Mọi lần tải, kể cả bị từ chối hay hỏng, đều ghi nhật ký truy cập (chỉ id và kết quả).
public sealed class HandoverDownloadService : IHandoverDownloadService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IAssetContentStore contentStore;
    private readonly IHoldReader holdReader;
    private readonly IExecutorKeyShardRepository? keyShards;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverDownloadService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IHandoverRepository handover,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
        : this(currentUser, vaults, handover, contentStore, new NoHoldReader(), unitOfWork, timeProvider)
    {
    }

    public HandoverDownloadService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IHandoverRepository handover,
        IAssetContentStore contentStore,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IExecutorKeyShardRepository? keyShards = null)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.handover = handover;
        this.contentStore = contentStore;
        this.holdReader = holdReader;
        this.keyShards = keyShards;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<PreparedDownload> PrepareAsync(Guid grantId, Guid itemId, CancellationToken ct)
    {
        // 1. Người đang đăng nhập
        Guid userId = currentUser.RequireUserId();

        // 2. Tìm quyền tải và tài sản trong gói; không phải của mình thì báo không tìm thấy
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

        // 3. Quyền còn hiệu lực không: hết hạn, bị thu hồi, kho đang bị giữ, hoặc chưa nhập khóa mở gói.
        //    Bị chặn thì ghi nhật ký và báo lý do
        DateTimeOffset now = timeProvider.GetUtcNow();
        string? deniedReason = HandoverRules.GrantDeniedReason(grant, vault.Plan.Status, now);
        if (deniedReason == null && await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            // Hold đang hiệu lực chặn tải (SRS §6.4), kể cả hold Admin đặt không đổi trạng thái grant.
            deniedReason = "HOLD_ACTIVE";
        }
        if (deniedReason == null && turn.KeyVerifiedAt == null && keyShards != null
            && await keyShards.FindByVaultIdAsync(turn.VaultId, ct) != null)
        {
            // Người nhận phải nhập khóa mở gói do Người thực thi đưa trước khi tải.
            deniedReason = "KEY_REQUIRED";
        }
        if (deniedReason != null)
        {
            await WriteLogAsync(grant, item, userId, HandoverAccessOutcome.Denied, now, ct);

            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = deniedReason;
            throw new AppException(AppErrorType.Forbidden, ErrorCode.GrantNotActive,
                "Quyền tải tài sản không còn hiệu lực.", extensions);
        }

        // 4. Nội dung phải còn: không thấy tài sản hoặc đã bị xóa theo hạn lưu thì báo lỗi
        Asset? asset = await handover.FindAssetAsync(item.AssetId, ct);
        if (asset == null)
        {
            await WriteLogAsync(grant, item, userId, HandoverAccessOutcome.IntegrityFailed, now, ct);
            throw new ContentIntegrityException("Không tìm thấy nội dung của tài sản.", null);
        }
        if (asset.ContentDeletedAt != null)
        {
            // RETENTION-06: nội dung đã dọn (khóa đã hủy) thì không giải mã nữa.
            Dictionary<string, object?> deleted = new Dictionary<string, object?>();
            deleted["reason"] = "CONTENT_DELETED";
            throw new AppException(AppErrorType.Forbidden, ErrorCode.GrantNotActive,
                "Nội dung đã được xóa theo chính sách lưu dữ liệu.", deleted);
        }

        // 5. Giải mã thử một lượt để chắc tệp không hỏng, ghi nhật ký thành công
        StoredContent stored = new StoredContent();
        stored.StorageKey = asset.StorageKey;
        stored.SizeBytes = asset.SizeBytes;
        stored.EncryptionKeyId = asset.EncryptionKeyId;
        stored.WrappedDataKey = asset.WrappedDataKey;

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

        // 6. Trả thông tin tệp; nội dung được giải mã lần nữa khi ghi ra response
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
