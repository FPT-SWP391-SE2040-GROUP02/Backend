using System.Security.Cryptography;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Kết quả một lần xử lý kế hoạch của job lưu dữ liệu (để log/test).</summary>
public sealed class RetentionRunResult
{
    public int TurnsDeleted { get; set; }

    public int VersionsShredded { get; set; }

    public int NoticesSent { get; set; }

    public bool VaultClosed { get; set; }
}

/// <summary>Việc nền lưu và xóa dữ liệu sau bàn giao (SRS §7.7, RETENTION-01..09, SETUP-18).</summary>
public interface IRetentionJob
{
    Task<List<Guid>> FindPlanIdsAsync(CancellationToken ct);

    /// <summary>Xử lý một kế hoạch; đọc lại và kiểm lại mọi điều kiện nên chạy lặp an toàn.</summary>
    Task<RetentionRunResult> ProcessPlanAsync(Guid planId, CancellationToken ct);
}

/// <remarks>
/// - Đồng hồ theo từng lượt (một chỉ định). Lý do giữ: còn trong 5 ngày phản hồi, claim đang xử lý, grant còn hạn,
///   Owner báo còn sống (kế hoạch BLOCKED). Tạm dừng lưu phần còn lại, không đặt lại 30 ngày.
/// - RETENTION-02: phiên bản tài sản chỉ bị xóa khi KHÔNG còn lượt nào khác chưa dọn cùng tham chiếu.
/// - RETENTION-06: mọi kiểm tra và thay đổi DB của một kế hoạch lưu trong MỘT lần lưu và ghi plan.UpdatedAt, nên nhận lượt
///   hay đồng ý chạy cùng lúc (cũng ghi plan.UpdatedAt) thua RowVersion: không có grant mới trên dữ liệu vừa dọn.
/// - Xóa an toàn: hủy khóa trước (ghi đè WrappedDataKey bằng 0 + ContentDeletedAt) trong giao dịch, xóa tệp sau khi lưu;
///   xóa tệp lỗi thì nội dung vẫn không giải mã được và vòng sau thử xóa lại.
/// - Lượt còn mở khi dọn thành LAPSED, link mời bị vô hiệu (đổi mã băm token).
/// - SETUP-18: kế hoạch CLOSED và mọi lượt đã dọn thì xóa nốt nội dung còn lại của kho và đóng kho.
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
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public RetentionJob(
        IRetentionRepository repository,
        IVaultRepository vaults,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IAssetContentStore contentStore,
        IRetentionNoticeSender sender,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.repository = repository;
        this.vaults = vaults;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.contentStore = contentStore;
        this.sender = sender;
        this.holdReader = holdReader;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<List<Guid>> FindPlanIdsAsync(CancellationToken ct)
    {
        return repository.FindPlanIdsForRetentionAsync(BatchSize, ct);
    }

    public async Task<RetentionRunResult> ProcessPlanAsync(Guid planId, CancellationToken ct)
    {
        RetentionRunResult result = new RetentionRunResult();
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
        List<HandoverTurn> due = new List<HandoverTurn>();
        bool vaultHeld = await holdReader.HasActiveHoldAsync(vault.Id, ct);
        foreach (HandoverTurn turn in turns)
        {
            if (!RetentionRules.AppliesToTurn(turn))
            {
                continue;
            }
            string? reason = RetentionRules.HoldReason(turn, plan.Status, now);
            if (reason == null && vaultHeld)
            {
                // SRS §6.4: hold đang hiệu lực giữ dữ liệu; tạm dừng đồng hồ lưu, không xóa.
                reason = RetentionRules.HoldAdmin;
            }
            if (RetentionRules.Advance(turn, reason, now))
            {
                due.Add(turn);
            }
        }

        if (due.Count > 0)
        {
            await DeleteTurnsAsync(turns, due, now, result, ct);
            plan.UpdatedAt = now;
            HandoverRules.CloseIfAllTerminal(plan, turns, now);
        }

        if (plan.Status == PlanStatus.Closed && AllTurnsDone(turns))
        {
            // SETUP-18: mọi lượt đã dọn và kế hoạch đã đóng → dọn nốt nội dung còn lại rồi đóng kho.
            List<AssetVersion> rest = await repository.FindLiveVersionsOfVaultAsync(vault.Id, ct);
            foreach (AssetVersion version in rest)
            {
                // Truy vấn đọc theo DB nên có thể trả lại phiên bản vừa hủy khóa ở trên (chưa lưu): bỏ qua.
                if (version.ContentDeletedAt != null)
                {
                    continue;
                }
                Shred(version, now);
                result.VersionsShredded++;
            }
            vault.Status = VaultStatus.Closed;
            vault.ClosedAt = now;
            vault.UpdatedAt = now;
            result.VaultClosed = true;
        }

        await unitOfWork.SaveChangesAsync(ct);

        await DeleteStoredFilesAsync(vault.Id, now, ct);
        result.NoticesSent = await SendNoticesAsync(vault, turns, now, ct);
        return result;
    }

    /// <summary>Dọn các lượt tới hạn; phiên bản còn lượt khác giữ thì để lại (RETENTION-02).</summary>
    private async Task DeleteTurnsAsync(
        List<HandoverTurn> turns, List<HandoverTurn> due, DateTimeOffset now, RetentionRunResult result, CancellationToken ct)
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
                stillReferenced.Add(item.AssetVersionId);
            }
        }

        List<Guid> toShred = new List<Guid>();
        foreach (HandoverTurn turn in due)
        {
            foreach (HandoverSnapshotItem item in turn.Items)
            {
                if (!stillReferenced.Contains(item.AssetVersionId) && !toShred.Contains(item.AssetVersionId))
                {
                    toShred.Add(item.AssetVersionId);
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
            result.TurnsDeleted++;
        }

        if (toShred.Count > 0)
        {
            List<AssetVersion> versions = await repository.FindLiveVersionsAsync(toShred, ct);
            foreach (AssetVersion version in versions)
            {
                Shred(version, now);
                result.VersionsShredded++;
            }
        }
    }

    /// <summary>Hủy khóa nội dung: ghi đè DEK đã bọc bằng 0, ghi dấu đã xóa. Tệp xóa sau khi lưu.</summary>
    private static void Shred(AssetVersion version, DateTimeOffset now)
    {
        version.WrappedDataKey = new byte[version.WrappedDataKey.Length];
        version.ContentDeletedAt = now;
    }

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

    private async Task DeleteStoredFilesAsync(Guid vaultId, DateTimeOffset now, CancellationToken ct)
    {
        List<AssetVersion> pending = await repository.FindPendingStorageDeletesAsync(vaultId, ct);
        if (pending.Count == 0)
        {
            return;
        }
        foreach (AssetVersion version in pending)
        {
            try
            {
                await contentStore.DeleteAsync(version.StorageKey, ct);
                version.StorageDeletedAt = now;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Khóa đã hủy nên nội dung không mở được; vòng sau thử xóa tệp lại.
            }
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

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

            RetentionNotice notice = new RetentionNotice();
            notice.TurnId = turn.Id;
            notice.ToEmail = data.Email;
            notice.FullName = data.FullName;
            notice.Stage = stage;
            notice.DeleteAt = turn.RetentionDeadline!.Value;
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
