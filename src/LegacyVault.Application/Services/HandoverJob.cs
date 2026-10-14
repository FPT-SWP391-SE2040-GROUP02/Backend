using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

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

    public async Task ProcessResponseExpiryAsync(Guid turnId, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (turn == null || turn.Status != HandoverTurnStatus.Invited || !HandoverRules.IsExpired(turn.ResponseDeadline, now))
        {
            return;
        }

        // Chưa kết thúc: người nhận vẫn có thể nhận lượt sau hạn này, nên không đóng kế hoạch.
        turn.Status = HandoverTurnStatus.InitialResponseExpired;
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ProcessDecisionExpiryAsync(Guid turnId, CancellationToken ct)
    {
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

        turn.Status = HandoverTurnStatus.DecisionExpired;
        turn.EndedAt = now;
        turn.UpdatedAt = now;

        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault != null)
        {
            List<HandoverTurn> turns = await handover.FindTurnsByPlanAsync(vault.Plan.Id, ct);
            HandoverRules.CloseIfAllTerminal(vault.Plan, turns, now);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ProcessGrantExpiryAsync(Guid grantId, CancellationToken ct)
    {
        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (grant == null || grant.Status != HandoverGrantStatus.Active || !HandoverRules.IsExpired(grant.ExpiresAt, now))
        {
            return;
        }

        grant.Status = HandoverGrantStatus.Expired;
        grant.EndedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
