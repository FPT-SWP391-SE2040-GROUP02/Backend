using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// TIME-04, TIME-09: kế hoạch đang bị chặn (Blocked) hoặc kho có hold thì đồng hồ tạm dừng: job không chuyển lượt hay grant
/// sang hết hạn. Khi hold được gỡ, HandoverClockRules.ExtendAfterPause dời các hạn đang chạy đúng bằng thời gian đã dừng.
/// </remarks>
// Job chạy nền xử lý các mốc hết hạn của bàn giao, mỗi loại hai bước: tìm id rồi xử lý từng id.
//   1. Quá hạn phản hồi thư mời ..... FindExpiredResponseTurnIdsAsync, ProcessResponseExpiryAsync
//   2. Quá hạn quyết định (luồng cũ) . FindExpiredDecisionTurnIdsAsync, ProcessDecisionExpiryAsync
//   3. Hết hạn quyền tải ............ FindExpiredGrantIdsAsync, ProcessGrantExpiryAsync
// Kho đang bị giữ thì đồng hồ dừng: job bỏ qua, không đổi gì.
public sealed class HandoverJob : IHandoverJob
{
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverJob(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
        : this(vaults, handover, new NoHoldReader(), unitOfWork, timeProvider)
    {
    }

    public HandoverJob(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.holdReader = holdReader;
        this.vaults = vaults;
        this.handover = handover;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(CancellationToken ct)
    {
        return handover.FindExpiredResponseTurnIdsAsync(timeProvider.GetUtcNow(), ct);
    }

    public Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(CancellationToken ct)
    {
        return handover.FindExpiredDecisionTurnIdsAsync(timeProvider.GetUtcNow(), ct);
    }

    public Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(CancellationToken ct)
    {
        return handover.FindExpiredGrantIdsAsync(timeProvider.GetUtcNow(), ct);
    }

    // Lượt quá hạn phản hồi: đánh dấu đã quá hạn (người nhận vẫn nhận lượt được sau đó).
    public async Task ProcessResponseExpiryAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải còn ở trạng thái đã mời và thật sự quá hạn; kho bị giữ thì bỏ qua
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (turn == null || turn.Status != HandoverTurnStatus.Invited || !HandoverRules.IsExpired(turn.ResponseDeadline, now))
        {
            return;
        }

        if (await IsClockPausedAsync(turn.VaultId, ct))
        {
            return;
        }

        // Chưa kết thúc: người nhận vẫn có thể nhận lượt sau hạn này, nên không đóng kế hoạch.
        // 2. Đổi trạng thái rồi lưu
        turn.Status = HandoverTurnStatus.InitialResponseExpired;
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    // Luồng cũ: lượt quá hạn quyết định thì kết thúc; đóng kế hoạch nếu mọi lượt đã xong.
    public async Task ProcessDecisionExpiryAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải đang chờ quyết định và thật sự quá hạn; kho bị giữ thì bỏ qua
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (turn == null || turn.Status != HandoverTurnStatus.ReadyToAccept)
        {
            return;
        }
        if (turn.DecisionDeadline == null || !HandoverRules.IsExpired(turn.DecisionDeadline.Value, now))
        {
            return;
        }

        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault != null && await IsClockPausedAsync(vault, ct))
        {
            return;
        }

        // 2. Kết thúc lượt, đóng kế hoạch nếu mọi lượt đã xong, rồi lưu
        turn.Status = HandoverTurnStatus.DecisionExpired;
        turn.EndedAt = now;
        turn.UpdatedAt = now;

        if (vault != null)
        {
            List<HandoverTurn> turns = await handover.FindTurnsByPlanAsync(vault.Plan.Id, ct);
            HandoverRules.CloseIfAllTerminal(vault.Plan, turns, now);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    // Quyền tải hết hạn: đánh dấu hết hạn.
    public async Task ProcessGrantExpiryAsync(Guid grantId, CancellationToken ct)
    {
        // 1. Quyền phải còn hiệu lực và thật sự quá hạn; kho bị giữ thì bỏ qua
        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (grant == null || grant.Status != HandoverGrantStatus.Active || !HandoverRules.IsExpired(grant.ExpiresAt, now))
        {
            return;
        }

        if (await IsClockPausedAsync(grant.Turn.VaultId, ct))
        {
            return;
        }

        // 2. Đổi trạng thái rồi lưu
        grant.Status = HandoverGrantStatus.Expired;
        grant.EndedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<bool> IsClockPausedAsync(Guid vaultId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        return vault != null && await IsClockPausedAsync(vault, ct);
    }

    private async Task<bool> IsClockPausedAsync(Vault vault, CancellationToken ct)
    {
        return vault.Plan.Status == PlanStatus.Blocked || await holdReader.HasActiveHoldAsync(vault.Id, ct);
    }
}
