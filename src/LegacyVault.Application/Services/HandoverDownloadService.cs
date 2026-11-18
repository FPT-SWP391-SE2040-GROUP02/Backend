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
    private readonly IExecutorKeyShardRepository keyShards;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverDownloadService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IHandoverRepository handover,
        IAssetContentStore contentStore,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IExecutorKeyShardRepository keyShards)
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

    /// <summary>
    /// Nút "Tải" một tài sản: kiểm quyền, kiểm toàn vẹn nội dung rồi trả thông tin tệp. Chưa có byte nội dung nào được gửi
    /// trước khi kiểm xong.
    /// </summary>
    public async Task<PreparedDownload> PrepareAsync(Guid grantId, Guid itemId, CancellationToken ct)
    {
        // 1. Người đang đăng nhập
        Guid userId = currentUser.RequireUserId();

        // 2. Tìm quyền tải; quyền lạ và quyền của người khác cho cùng một lỗi, không ghi nhật ký
        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        if (grant == null || grant.UserId != userId)
        {
            throw HandoverAccess.NotFound();
        }

        // 3. Tìm tài sản trong gói của lượt và kho của lượt
        HandoverTurn turn = grant.Turn;
        HandoverSnapshotItem? item = null;
        foreach (HandoverSnapshotItem candidate in turn.Items)
        {
            if (candidate.Id == itemId)
            {
                item = candidate;
                break;
            }
        }
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (item == null || vault == null)
        {
            throw HandoverAccess.NotFound();
        }

        // 4. Quyền còn hiệu lực không: hết hạn, bị thu hồi, kho đang bị giữ, hoặc chưa nhập khóa mở gói.
        //    Bị chặn thì ghi nhật ký rồi báo lý do.
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverAccessLog log = new HandoverAccessLog
        {
            GrantId = grant.Id,
            SnapshotItemId = item.Id,
            UserId = userId,
            At = now
        };
        string? deniedReason = HandoverRules.GrantDeniedReason(grant, vault.Plan.Status, now);
        if (deniedReason == null)
        {
            bool vaultIsHeld = await holdReader.HasActiveHoldAsync(vault.Id, ct);
            if (vaultIsHeld)
            {
                // Kho đang bị giữ thì không cho tải, kể cả khi quyền tải vẫn còn hạn.
                deniedReason = "HOLD_ACTIVE";
            }
        }
        if (deniedReason == null && turn.KeyVerifiedAt == null)
        {
            ExecutorKeyShard? shard = await keyShards.FindByVaultIdAsync(turn.VaultId, ct);
            if (shard != null)
            {
                // Kho có khóa mở gói thì người nhận phải nhập khóa do Người thực thi đưa trước khi tải.
                deniedReason = "KEY_REQUIRED";
            }
        }
        if (deniedReason != null)
        {
            await SaveLogAsync(log, HandoverAccessOutcome.Denied, ct);
            throw GrantNotActive(deniedReason, "Quyền tải tài sản không còn hiệu lực.");
        }

        // 5. Nội dung phải còn: không thấy tài sản hoặc đã bị xóa theo hạn lưu thì báo lỗi
        Asset? asset = await handover.FindAssetAsync(item.AssetId, ct);
        if (asset == null)
        {
            await SaveLogAsync(log, HandoverAccessOutcome.IntegrityFailed, ct);
            throw new ContentIntegrityException("Không tìm thấy nội dung của tài sản.", null);
        }
        if (asset.ContentDeletedAt != null)
        {
            // Nội dung đã dọn: khóa giải mã đã bị hủy nên không thể giải mã nữa.
            throw GrantNotActive("CONTENT_DELETED", "Nội dung đã được xóa theo chính sách lưu dữ liệu.");
        }

        // 6. Giải mã thử một lượt vào Stream.Null để chắc tệp không hỏng.
        //    Việc kiểm kích thước và SHA-256 chỉ xảy ra ở cuối luồng, nên phải chạy hết một lượt
        //    trước khi gửi byte đầu tiên, nếu không người nhận có thể nhận tệp hỏng.
        StoredContent stored = new StoredContent
        {
            StorageKey = asset.StorageKey,
            SizeBytes = asset.SizeBytes,
            EncryptionKeyId = asset.EncryptionKeyId,
            WrappedDataKey = asset.WrappedDataKey
        };
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            await SaveLogAsync(log, HandoverAccessOutcome.IntegrityFailed, ct);
            throw new ContentIntegrityException("Nội dung tài sản không vượt qua kiểm tra toàn vẹn.", ex);
        }
        await SaveLogAsync(log, HandoverAccessOutcome.Succeeded, ct);

        // 7. Trả thông tin tệp. Nội dung được giải mã lần hai khi ghi thẳng ra response.
        PreparedDownload download = new PreparedDownload
        {
            FileName = ResolveFileName(item),
            ContentType = item.ContentType,
            Length = item.SizeBytes,
            WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token)
        };
        return download;
    }

    /// <summary>Ghi một dòng nhật ký truy cập. Chỉ ghi id và kết quả, không ghi tên tệp hay nội dung.</summary>
    private async Task SaveLogAsync(HandoverAccessLog log, HandoverAccessOutcome outcome, CancellationToken ct)
    {
        log.Outcome = outcome;
        handover.AddAccessLog(log);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Tên tệp trả về: tên tệp gốc; tài sản dạng văn bản thì lấy tên tài sản kèm đuôi .txt.</summary>
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

    /// <summary>Lỗi 403: quyền tải không dùng được, kèm mã lý do để giao diện hiện câu phù hợp.</summary>
    private static AppException GrantNotActive(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Forbidden, ErrorCode.GrantNotActive, message, extensions);
    }
}
