using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Dùng context.Set&lt;T&gt;() thay vì DbSet trên AppDbContext để không sửa AppDbContext
/// (giảm xung đột khi gộp với các nhánh khác cùng thêm DbSet).
/// </remarks>
public sealed class HandoverRepository : IHandoverRepository
{
    private readonly AppDbContext context;

    public HandoverRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<HandoverTurn?> FindTurnByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Include(t => t.Items)
            .Include(t => t.Grant)
            .FirstOrDefaultAsync(t => t.InvitationTokenHash == tokenHash, ct);
    }

    public Task<HandoverTurn?> FindTurnByIdAsync(Guid turnId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Include(t => t.Items)
            .Include(t => t.Grant)
            .FirstOrDefaultAsync(t => t.Id == turnId, ct);
    }

    public Task<List<HandoverTurn>> FindTurnsByClaimantAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Include(t => t.Items)
            .Include(t => t.Grant)
            .Where(t => t.ClaimantUserId == userId)
            .OrderByDescending(t => t.InvitedAt)
            .ThenByDescending(t => t.Id)
            .ToListAsync(ct);
    }

    public Task<List<HandoverTurn>> FindTurnsByPlanAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Where(t => t.PlanId == planId)
            .ToListAsync(ct);
    }

    public Task<HandoverGrant?> FindGrantByIdAsync(Guid grantId, CancellationToken ct)
    {
        return context.Set<HandoverGrant>()
            .Include(g => g.Turn).ThenInclude(t => t.Items)
            .FirstOrDefaultAsync(g => g.Id == grantId, ct);
    }

    public Task<List<Designation>> FindStartableDesignationsAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Designation>()
            .Include(d => d.Beneficiary)
            .Include(d => d.Container).ThenInclude(c => c.Assets).ThenInclude(ca => ca.Asset)
            .Where(d => d.Container.VaultId == vaultId && d.Container.Status == ContainerStatus.Ready)
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);
    }

    public Task<Asset?> FindAssetAsync(Guid assetId, CancellationToken ct)
    {
        return context.Assets.FirstOrDefaultAsync(a => a.Id == assetId, ct);
    }

    public async Task<IReadOnlyList<Guid>> FindUndeliveredInvitationTurnIdsAsync(
        DateTimeOffset createdBefore, int maxAttempts, CancellationToken ct)
    {
        List<Guid> ids = await context.Set<HandoverTurn>()
            .Where(t => t.Status == HandoverTurnStatus.Invited
                && t.InvitationSentAt == null
                && t.ClaimantUserId == null
                && t.CreatedAt <= createdBefore
                && t.InvitationAttempts < maxAttempts)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> ids = await context.Set<HandoverTurn>()
            .Where(t => t.Status == HandoverTurnStatus.Invited && t.ResponseDeadline <= now)
            .OrderBy(t => t.ResponseDeadline)
            .Select(t => t.Id)
            .ToListAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> ids = await context.Set<HandoverTurn>()
            .Where(t => t.Status == HandoverTurnStatus.ReadyToAccept && t.DecisionDeadline != null && t.DecisionDeadline <= now)
            .OrderBy(t => t.DecisionDeadline)
            .Select(t => t.Id)
            .ToListAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> ids = await context.Set<HandoverGrant>()
            .Where(g => g.Status == HandoverGrantStatus.Active && g.ExpiresAt <= now)
            .OrderBy(g => g.ExpiresAt)
            .Select(g => g.Id)
            .ToListAsync(ct);
        return ids;
    }

    public bool IsUniqueViolation(Exception exception)
    {
        // SQL Server: 2601 = trùng giá trị chỉ mục unique, 2627 = vi phạm ràng buộc unique.
        if (exception is not DbUpdateException)
        {
            return false;
        }

        Exception? inner = exception.InnerException;
        while (inner != null)
        {
            if (inner is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }

    public void AddTurn(HandoverTurn turn)
    {
        context.Set<HandoverTurn>().Add(turn);
    }

    public void AddGrant(HandoverGrant grant)
    {
        context.Set<HandoverGrant>().Add(grant);
    }

    public void AddAccessLog(HandoverAccessLog log)
    {
        context.Set<HandoverAccessLog>().Add(log);
    }
}
