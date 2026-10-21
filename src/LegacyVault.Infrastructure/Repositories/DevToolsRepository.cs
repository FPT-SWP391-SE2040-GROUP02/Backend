using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class DevToolsRepository(AppDbContext context) : IDevToolsRepository
{
    public Task<List<Vault>> ListVaultsAsync(int maxCount, CancellationToken ct)
    {
        return context.Set<Vault>()
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Plan)
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public Task<Vault?> FindVaultAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Vault>()
            .Include(v => v.Plan)
            .FirstOrDefaultAsync(v => v.Id == vaultId, ct);
    }

    public Task<CheckInPeriod?> FindOpenPeriodAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<CheckInPeriod>()
            .Where(p => p.PlanId == planId && p.ClosedAt == null)
            .OrderByDescending(p => p.Sequence)
            .ThenBy(p => p.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<DeathCase?> FindOpenDeathCaseAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<DeathCase>()
            .Where(d => d.VaultId == vaultId && d.ClosedAt == null && d.Status != DeathCaseStatus.Rejected)
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<DeathCaseObjection?> FindLatestObjectionAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<DeathCaseObjection>()
            .Where(o => o.VaultId == vaultId)
            .OrderByDescending(o => o.FirstReportedAt)
            .ThenBy(o => o.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AuditAction?> FindLatestClockActionAsync(Guid vaultId, CancellationToken ct)
    {
        AuditLog? log = await context.Set<AuditLog>()
            .AsNoTracking()
            .Where(l => l.VaultId == vaultId
                && (l.Action == AuditAction.DevTimeTravel || l.Action == AuditAction.DevClockReset))
            .OrderByDescending(l => l.At)
            .ThenByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);
        if (log == null)
        {
            return null;
        }
        return log.Action;
    }
}
