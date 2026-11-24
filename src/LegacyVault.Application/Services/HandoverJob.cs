using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Job chạy nền xử lý các mốc hết hạn của bàn giao, mỗi loại hai bước: tìm id rồi xử lý từng id.
/// </summary>
/// <remarks>
/// 1. Quá hạn phản hồi thư mời: FindExpiredResponseTurnIdsAsync, ProcessResponseExpiryAsync.
/// 2. Quá hạn quyết định (luồng cũ): FindExpiredDecisionTurnIdsAsync, ProcessDecisionExpiryAsync.
/// 3. Hết hạn quyền tải: FindExpiredGrantIdsAsync, ProcessGrantExpiryAsync.
/// </remarks>
public sealed class HandoverJob : IHandoverJob
{
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverJob(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.vaults = vaults;
        this.handover = handover;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    /// <summary>Id các lượt đã mời nhưng quá hạn phản hồi.</summary>
    public Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(CancellationToken ct)
    {
        return handover.FindExpiredResponseTurnIdsAsync(timeProvider.GetUtcNow(), ct);
    }

    /// <summary>Id các lượt đang chờ quyết định (luồng cũ) nhưng quá hạn quyết định.</summary>
    public Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(CancellationToken ct)
    {
        return handover.FindExpiredDecisionTurnIdsAsync(timeProvider.GetUtcNow(), ct);
    }

    /// <summary>Id các quyền tải còn hiệu lực nhưng đã quá hạn.</summary>
    public Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(CancellationToken ct)
    {
        return handover.FindExpiredGrantIdsAsync(timeProvider.GetUtcNow(), ct);
    }

    /// <summary>Lượt quá hạn phản hồi: đánh dấu đã quá hạn (người nhận vẫn nhận lượt được sau đó).</summary>
    public async Task ProcessResponseExpiryAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải còn ở trạng thái đã mời và thật sự quá hạn
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (turn == null || turn.Status != HandoverTurnStatus.Invited || !HandoverRules.IsExpired(turn.ResponseDeadline, now))
        {
            return;
        }

        // Chưa kết thúc: người nhận vẫn có thể nhận lượt sau hạn này, nên không đóng kế hoạch.
        // 2. Đổi trạng thái rồi lưu
        turn.Status = HandoverTurnStatus.InitialResponseExpired;
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Luồng cũ: lượt quá hạn quyết định thì kết thúc; đóng kế hoạch nếu mọi lượt đã xong.</summary>
    public async Task ProcessDecisionExpiryAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải đang chờ quyết định và thật sự quá hạn
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

    /// <summary>Quyền tải quá hạn: đánh dấu hết hạn.</summary>
    public async Task ProcessGrantExpiryAsync(Guid grantId, CancellationToken ct)
    {
        // 1. Quyền phải còn hiệu lực và thật sự quá hạn
        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (grant == null || grant.Status != HandoverGrantStatus.Active || !HandoverRules.IsExpired(grant.ExpiresAt, now))
        {
            return;
        }

        // 2. Đổi trạng thái rồi lưu
        grant.Status = HandoverGrantStatus.Expired;
        grant.EndedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
