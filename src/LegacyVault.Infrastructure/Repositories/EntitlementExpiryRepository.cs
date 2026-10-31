using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class EntitlementExpiryRepository : IEntitlementExpiryRepository
{
    private readonly AppDbContext context;

    public EntitlementExpiryRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredPaidVaultIdsAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        List<Guid> ids = await context.Set<VaultEntitlement>()
            .AsNoTracking()
            .Where(e => e.Status == EntitlementStatus.PaidActive
                && e.ServiceExpiresAt != null
                && e.ServiceExpiresAt <= now)
            .OrderBy(e => e.ServiceExpiresAt)
            .Select(e => e.VaultId)
            .Take(max)
            .ToListAsync(ct);
        return ids;
    }
}
