using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class RetentionRepository : IRetentionRepository
{
    private readonly AppDbContext context;

    public RetentionRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<List<Guid>> FindPlanIdsForRetentionAsync(int maxCount, CancellationToken ct)
    {
        return context.HandoverPlans
            .Where(p => (p.Status == PlanStatus.HandoverInProgress
                    || p.Status == PlanStatus.Closed
                    || p.Status == PlanStatus.Blocked)
                && p.Vault.Status != VaultStatus.Closed
                && context.Set<HandoverTurn>().Any(t => t.PlanId == p.Id))
            .OrderBy(p => p.UpdatedAt)
            .ThenBy(p => p.Id)
            .Select(p => p.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public Task<List<HandoverTurn>> FindTurnsAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Include(t => t.Items)
            .Include(t => t.Grant)
            .Where(t => t.PlanId == planId)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
    }

    public Task<List<Asset>> FindLiveAssetsAsync(List<Guid> assetIds, CancellationToken ct)
    {
        return context.Assets
            .Where(a => assetIds.Contains(a.Id) && a.ContentDeletedAt == null)
            .ToListAsync(ct);
    }

    public Task<List<Asset>> FindLiveAssetsOfVaultAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Assets
            .Where(a => a.VaultId == vaultId && a.ContentDeletedAt == null)
            .ToListAsync(ct);
    }

    public Task<List<Asset>> FindPendingStorageDeletesAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Assets
            .Where(a => a.VaultId == vaultId && a.ContentDeletedAt != null && a.StorageDeletedAt == null)
            .ToListAsync(ct);
    }
}
