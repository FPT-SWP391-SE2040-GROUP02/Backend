using System.Security.Cryptography;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Việc nền lưu và xóa dữ liệu sau bàn giao.</summary>
/// <remarks>
/// - Đồng hồ theo từng lượt (một chỉ định). Lý do giữ: còn trong 5 ngày phản hồi, claim đang xử lý, grant còn hạn.
///   Tạm dừng lưu phần còn lại, không đặt lại 30 ngày.
/// - Phiên bản tài sản chỉ bị xóa khi KHÔNG còn lượt nào khác chưa dọn cùng tham chiếu.
/// - Mọi kiểm tra và thay đổi DB của một kế hoạch lưu trong MỘT lần lưu và ghi plan.UpdatedAt, nên nhận lượt
///   hay đồng ý chạy cùng lúc (cũng ghi plan.UpdatedAt) thua RowVersion: không có grant mới trên dữ liệu vừa dọn.
/// - Xóa an toàn: hủy khóa trước (ghi đè WrappedDataKey bằng 0 + ContentDeletedAt) trong giao dịch, xóa tệp sau khi lưu;
///   xóa tệp lỗi thì nội dung vẫn không giải mã được và vòng sau thử xóa lại.
/// - Lượt còn mở khi dọn thành LAPSED, link mời bị vô hiệu (đổi mã băm token).
/// - Kế hoạch CLOSED và mọi lượt đã dọn thì xóa nốt nội dung còn lại của kho và đóng kho.
/// - Kế hoạch đã về ACTIVE (Owner còn sống) hay lượt CANCELLED không bao giờ bị dọn.
/// - TODO(LV-07): thư mốc gửi lỗi phải giao người xử lý có hạn (RETENTION-05); hiện vòng sau gửi lại.
/// </remarks>
public sealed class RetentionJob : IRetentionJob
{
    private const int BatchSize = 20;

    private readonly IRetentionRepository repository;
    private readonly IVaultRepository vaults;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IAssetContentStore contentStore;
    private readonly IRetentionNoticeSender sender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IHandoverSubmissionRepository? submissions;

    public RetentionJob(
        IRetentionRepository repository,
        IVaultRepository vaults,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IAssetContentStore contentStore,
        IRetentionNoticeSender sender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IHandoverSubmissionRepository? submissions = null)
    {
        this.repository = repository;
        this.vaults = vaults;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.contentStore = contentStore;
        this.sender = sender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.submissions = submissions;
    }

    /// <summary>Lấy một lô kế hoạch cần xét; số lượng mỗi lô cố định là BatchSize.</summary>
    public Task<List<Guid>> FindPlanIdsAsync(CancellationToken ct)
    {
        return repository.FindPlanIdsForRetentionAsync(BatchSize, ct);
    }

    // Job dọn dữ liệu cho một kế hoạch: hết thời hạn lưu thì xóa nội dung lượt, dọn xong hết thì đóng kho.
    public async Task<RetentionRunResult> ProcessPlanAsync(Guid planId, CancellationToken ct)
    {
        RetentionRunResult result = new RetentionRunResult();
        // 1. Lấy các lượt và kho; kho đã đóng hoặc kế hoạch chưa tới giai đoạn lưu/xóa thì bỏ qua
        List<HandoverTurn> turns = await repository.FindTurnsAsync(planId, ct);
        if (turns.Count == 0)
        {
            return result;
        }
        Vault? vault = await vaults.FindByIdAsync(turns[0].VaultId, ct);
        if (vault == null || vault.Status == VaultStatus.Closed)
        {
            return result;
        }
        HandoverPlan plan = vault.Plan;
        if (!RetentionRules.AppliesToPlan(plan.Status))
        {
            return result;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        // 2. Với từng lượt: còn lý do giữ (đang nhận, còn quyền tải...) thì dừng đồng hồ, hết lý do thì chạy tiếp;
        //    lượt hết thời hạn lưu được đưa vào danh sách cần dọn
        List<HandoverTurn> due = new List<HandoverTurn>();
        foreach (HandoverTurn turn in turns)
        {
            if (!RetentionRules.AppliesToTurn(turn))
            {
                continue;
            }
            string? reason = RetentionRules.HoldReason(turn, plan.Status, now);
            if (RetentionRules.Advance(turn, reason, now))
            {
                due.Add(turn);
            }
        }

        // 3. Dọn các lượt tới hạn (tài sản còn lượt khác dùng thì giữ), đóng kế hoạch nếu mọi lượt đã xong
        if (due.Count > 0)
        {
            HashSet<Guid> stillReferenced = FindAssetsStillReferenced(turns, due);
            List<Guid> assetIdsToShred = MarkTurnsDeleted(due, stillReferenced, now);
            result.TurnsDeleted += due.Count;
            result.AssetsShredded += await ShredAssetsAsync(assetIdsToShred, now, ct);
            plan.UpdatedAt = now;
            HandoverRules.CloseIfAllTerminal(plan, turns, now);
        }

        // 4. Kế hoạch đã đóng và mọi lượt đã dọn: hủy nốt nội dung còn lại và đóng kho
        if (plan.Status == PlanStatus.Closed && AllTurnsDone(turns))
        {
            // Mọi lượt đã dọn và kế hoạch đã đóng → dọn nốt nội dung còn lại rồi đóng kho.
            List<Asset> rest = await repository.FindLiveAssetsOfVaultAsync(vault.Id, ct);
            foreach (Asset asset in rest)
            {
                // Truy vấn đọc theo DB nên có thể trả lại tài sản vừa hủy khóa ở trên (chưa lưu): bỏ qua.
                if (asset.ContentDeletedAt != null)
                {
                    continue;
                }
                Shred(asset, now);
                result.AssetsShredded++;
            }
            vault.Status = VaultStatus.Closed;
            vault.ClosedAt = now;
            vault.UpdatedAt = now;
            result.VaultClosed = true;
        }

        // 5. Lưu một lần; sau đó mới xóa tệp, xóa ảnh căn cước của lượt đã dọn và gửi thư nhắc
        await unitOfWork.SaveChangesAsync(ct);

        await DeleteStoredFilesAsync(vault.Id, now, ct);
        await DeleteSubmissionImagesAsync(due, ct);
        result.NoticesSent = await SendNoticesAsync(vault, turns, now, ct);
        return result;
    }

    /// <summary>
    /// Id các tài sản vẫn còn được một lượt khác (chưa dọn, không bị hủy) tham chiếu. Tài sản trong danh sách này
    /// không được hủy khóa dù lượt đang dọn cũng dùng nó.
    /// </summary>
    private static HashSet<Guid> FindAssetsStillReferenced(List<HandoverTurn> turns, List<HandoverTurn> due)
    {
        HashSet<Guid> dueIds = new HashSet<Guid>();
        foreach (HandoverTurn turn in due)
        {
            dueIds.Add(turn.Id);
        }

        HashSet<Guid> stillReferenced = new HashSet<Guid>();
        foreach (HandoverTurn turn in turns)
        {
            if (dueIds.Contains(turn.Id) || !RetentionRules.AppliesToTurn(turn))
            {
                continue;
            }
            foreach (HandoverSnapshotItem item in turn.Items)
            {
                stillReferenced.Add(item.AssetId);
            }
        }
        return stillReferenced;
    }

    /// <summary>
    /// Đánh dấu các lượt tới hạn là đã xóa: vô hiệu link mời, lượt còn mở thì chuyển sang hết hạn.
    /// Trả về id các tài sản cần hủy khóa (tài sản còn lượt khác giữ thì không có trong danh sách).
    /// </summary>
    private static List<Guid> MarkTurnsDeleted(List<HandoverTurn> due, HashSet<Guid> stillReferenced, DateTimeOffset now)
    {
        List<Guid> toShred = new List<Guid>();
        foreach (HandoverTurn turn in due)
        {
            foreach (HandoverSnapshotItem item in turn.Items)
            {
                if (!stillReferenced.Contains(item.AssetId) && !toShred.Contains(item.AssetId))
                {
                    toShred.Add(item.AssetId);
                }
            }

            turn.RetentionStatus = RetentionStatus.Deleted;
            turn.DeletedAt = now;
            turn.RetentionDeadline = null;
            turn.RetentionRemainingSeconds = null;
            if (!HandoverRules.IsTerminal(turn.Status))
            {
                turn.Status = HandoverTurnStatus.Lapsed;
                turn.EndedAt = now;
            }
            // Vô hiệu link mời: token cũ không còn khớp mã băm nào.
            turn.InvitationTokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            turn.UpdatedAt = now;
        }
        return toShred;
    }

    /// <summary>Hủy khóa các tài sản theo id. Trả về số tài sản đã hủy.</summary>
    private async Task<int> ShredAssetsAsync(List<Guid> assetIds, DateTimeOffset now, CancellationToken ct)
    {
        if (assetIds.Count == 0)
        {
            return 0;
        }
        List<Asset> assets = await repository.FindLiveAssetsAsync(assetIds, ct);
        foreach (Asset asset in assets)
        {
            Shred(asset, now);
        }
        return assets.Count;
    }

    /// <summary>Hủy khóa nội dung: ghi đè DEK đã bọc bằng 0, ghi dấu đã xóa. Tệp xóa sau khi lưu.</summary>
    private static void Shred(Asset asset, DateTimeOffset now)
    {
        asset.WrappedDataKey = new byte[asset.WrappedDataKey.Length];
        asset.ContentDeletedAt = now;
    }

    /// <summary>Mọi lượt đều đã dọn xong hoặc đã bị hủy (Owner còn sống).</summary>
    private static bool AllTurnsDone(List<HandoverTurn> turns)
    {
        foreach (HandoverTurn turn in turns)
        {
            if (turn.Status != HandoverTurnStatus.Cancelled && turn.RetentionStatus != RetentionStatus.Deleted)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Lượt bị dọn thì ảnh căn cước người nhận đã nộp cũng bị xóa (giữ lại văn bản từ chối và biên bản đồng ý); để trống khóa để không xóa lại.</summary>
    private async Task DeleteSubmissionImagesAsync(List<HandoverTurn> due, CancellationToken ct)
    {
        if (submissions == null || due.Count == 0)
        {
            return;
        }
        List<Guid> turnIds = new List<Guid>();
        foreach (HandoverTurn turn in due)
        {
            turnIds.Add(turn.Id);
        }
        List<HandoverSubmission> found = await submissions.ListByTurnIdsAsync(turnIds, ct);
        bool changed = false;
        foreach (HandoverSubmission submission in found)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, submission.FrontImageStorageKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, submission.BackImageStorageKey);
            // Văn bản từ chối và biên bản đồng ý chỉ có họ tên, 4 số cuối căn cước: giữ lại làm bằng chứng, không xóa.
            submission.FrontImageStorageKey = string.Empty;
            submission.BackImageStorageKey = string.Empty;
            submission.FrontImageWrappedDataKey = new byte[submission.FrontImageWrappedDataKey.Length];
            submission.BackImageWrappedDataKey = new byte[submission.BackImageWrappedDataKey.Length];
            changed = true;
        }
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>Xóa tệp trên kho lưu trữ của các tài sản đã hủy khóa. Xóa lỗi thì để vòng sau thử lại.</summary>
    private async Task DeleteStoredFilesAsync(Guid vaultId, DateTimeOffset now, CancellationToken ct)
    {
        List<Asset> pending = await repository.FindPendingStorageDeletesAsync(vaultId, ct);
        if (pending.Count == 0)
        {
            return;
        }
        foreach (Asset asset in pending)
        {
            try
            {
                await contentStore.DeleteAsync(asset.StorageKey, ct);
                asset.StorageDeletedAt = now;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Khóa đã hủy nên nội dung không mở được; vòng sau thử xóa tệp lại.
            }
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Gửi thư nhắc mốc lưu dữ liệu cho người nhận của các lượt tới mốc. Trả về số thư đã gửi.</summary>
    private async Task<int> SendNoticesAsync(Vault vault, List<HandoverTurn> turns, DateTimeOffset now, CancellationToken ct)
    {
        int sent = 0;
        foreach (HandoverTurn turn in turns)
        {
            int stage = RetentionRules.DueNoticeStage(turn, now);
            if (stage == RetentionRules.NoticeNone || turn.Status == HandoverTurnStatus.Cancelled)
            {
                continue;
            }
            Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, turn.BeneficiaryId, ct);
            if (beneficiary == null)
            {
                continue;
            }
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            if (string.IsNullOrWhiteSpace(data.Email))
            {
                // Không có kênh email: ghi đã xét mốc để không lặp; TODO(LV-14): kênh khác / giao người xử lý.
                turn.RetentionNoticeStage = stage;
                continue;
            }

            RetentionNotice notice = new RetentionNotice
            {
                TurnId = turn.Id,
                ToEmail = data.Email,
                FullName = data.FullName,
                Stage = stage,
                DeleteAt = turn.RetentionDeadline!.Value
            };
            try
            {
                await sender.SendAsync(notice, ct);
                turn.RetentionNoticeStage = stage;
                sent++;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Vòng sau gửi lại.
            }
        }
        await unitOfWork.SaveChangesAsync(ct);
        return sent;
    }
}
