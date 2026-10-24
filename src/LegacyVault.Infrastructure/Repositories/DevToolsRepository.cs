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

    public Task<Vault?> FindVaultWithEntitlementAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Vault>()
            .Include(v => v.Plan)
            .Include(v => v.Entitlement)
            .FirstOrDefaultAsync(v => v.Id == vaultId, ct);
    }

    public Task<List<PaymentOrder>> ListPendingPaymentOrdersAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<PaymentOrder>()
            .Where(o => o.VaultId == vaultId && o.Status == PaymentOrderStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToListAsync(ct);
    }

    public Task<CheckInPeriod?> FindOpenPeriodAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<CheckInPeriod>()
            .Where(p => p.PlanId == planId && p.ClosedAt == null)
            .OrderByDescending(p => p.Sequence)
            .ThenBy(p => p.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<CheckInSchedule?> FindCheckInScheduleAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<CheckInSchedule>()
            .Where(s => s.PlanId == planId)
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

    public Task<List<EkycVerification>> ListEkycVerificationsAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<EkycVerification>()
            .Where(v => v.UserId == userId)
            .OrderBy(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .ToListAsync(ct);
    }

    public Task<List<EkycConsent>> ListEkycConsentsAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<EkycConsent>()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.ConsentedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<OwnerIdentity?> FindOwnerIdentityAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<OwnerIdentity>().FirstOrDefaultAsync(i => i.UserId == userId, ct);
    }

    public Task<List<OwnerFaceTemplate>> ListOwnerFaceTemplatesAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<OwnerFaceTemplate>()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Version)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
    }

    public void RemoveEkycData(
        List<EkycVerification> verifications, List<EkycConsent> consents,
        OwnerIdentity? identity, List<OwnerFaceTemplate> faceTemplates)
    {
        context.Set<EkycVerification>().RemoveRange(verifications);
        context.Set<EkycConsent>().RemoveRange(consents);
        if (identity != null)
        {
            context.Set<OwnerIdentity>().Remove(identity);
        }
        context.Set<OwnerFaceTemplate>().RemoveRange(faceTemplates);
    }
}
