using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class ExtraMailRepository : IExtraMailRepository
{
    private readonly AppDbContext context;

    public ExtraMailRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<List<ExtraMailRule>> ListAsync(CancellationToken ct)
    {
        return await context.ExtraMailRules.AsNoTracking().OrderBy(r => r.DaysFromDue).ThenBy(r => r.CreatedAt).ToListAsync(ct);
    }

    public async Task<List<ExtraMailRule>> ListEnabledAsync(CancellationToken ct)
    {
        return await context.ExtraMailRules.AsNoTracking().Where(r => r.Enabled).OrderBy(r => r.DaysFromDue).ToListAsync(ct);
    }

    public async Task<ExtraMailRule?> FindAsync(Guid id, CancellationToken ct)
    {
        return await context.ExtraMailRules.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        return await context.ExtraMailRules.CountAsync(ct);
    }

    public void Add(ExtraMailRule rule)
    {
        context.ExtraMailRules.Add(rule);
    }

    public void Remove(ExtraMailRule rule)
    {
        context.ExtraMailRules.Remove(rule);
    }

    public async Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(DateTimeOffset horizon, CancellationToken ct)
    {
        List<Guid> ids = await context.CheckInPeriods
            .Where(p => p.ClosedAt == null
                && p.DueAt <= horizon
                && (p.Plan.Status == PlanStatus.Active || p.Plan.Status == PlanStatus.CheckinOverdue))
            .OrderBy(p => p.DueAt)
            .Select(p => p.Id)
            .ToListAsync(ct);
        return ids;
    }

    public Task<CheckInPeriod?> FindPeriodAsync(Guid periodId, CancellationToken ct)
    {
        return context.CheckInPeriods
            .Include(p => p.Plan).ThenInclude(plan => plan.Vault).ThenInclude(v => v.Owner)
            .FirstOrDefaultAsync(p => p.Id == periodId, ct);
    }

    public async Task<List<Guid>> ListHandledRuleIdsAsync(Guid periodId, CancellationToken ct)
    {
        return await context.ExtraMailSends.AsNoTracking().Where(s => s.PeriodId == periodId).Select(s => s.RuleId).ToListAsync(ct);
    }

    public void AddSend(ExtraMailSend send)
    {
        context.ExtraMailSends.Add(send);
    }
}
