using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class CheckInRepository : ICheckInRepository
{
    /// <summary>Mốc nhắc sớm nhất là 7 ngày trước hạn: kỳ có hạn xa hơn chưa cần job xử lý.</summary>
    private static readonly TimeSpan EarliestReminderLead = TimeSpan.FromDays(7);

    private readonly AppDbContext context;

    public CheckInRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<CheckInSchedule?> FindScheduleByPlanIdAsync(Guid planId, CancellationToken ct)
    {
        return context.CheckInSchedules.FirstOrDefaultAsync(s => s.PlanId == planId, ct);
    }

    public Task<CheckInPeriod?> FindOpenPeriodByPlanIdAsync(Guid planId, CancellationToken ct)
    {
        return context.CheckInPeriods
            .FirstOrDefaultAsync(p => p.PlanId == planId && p.ClosedAt == null, ct);
    }

    public async Task<int> GetMaxSequenceAsync(Guid planId, CancellationToken ct)
    {
        int? max = await context.CheckInPeriods
            .Where(p => p.PlanId == planId)
            .MaxAsync(p => (int?)p.Sequence, ct);
        if (max == null)
        {
            return 0;
        }
        return max.Value;
    }

    public async Task<(List<CheckInPeriod> Items, int TotalCount)> FindPeriodPageByPlanIdAsync(
        Guid planId, int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<CheckInPeriod> query = context.CheckInPeriods
            .AsNoTracking()
            .Where(p => p.PlanId == planId);

        int totalCount = await query.CountAsync(ct);
        List<CheckInPeriod> items = await query
            .OrderByDescending(p => p.Sequence)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Guid>> FindOpenPeriodIdsForJobAsync(DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset horizon = now + EarliestReminderLead;
        List<Guid> ids = await context.CheckInPeriods
            .Where(p => p.ClosedAt == null
                && p.DueAt <= horizon
                && (p.Plan.Status == PlanStatus.Active || p.Plan.Status == PlanStatus.CheckinOverdue))
            .OrderBy(p => p.DueAt)
            .Select(p => p.Id)
            .ToListAsync(ct);
        return ids;
    }

    public Task<CheckInPeriod?> FindPeriodForJobAsync(Guid periodId, CancellationToken ct)
    {
        return context.CheckInPeriods
            .Include(p => p.Plan).ThenInclude(plan => plan.Vault).ThenInclude(v => v.Owner)
            .Include(p => p.Reminders)
            .FirstOrDefaultAsync(p => p.Id == periodId, ct);
    }

    public void AddSchedule(CheckInSchedule schedule)
    {
        context.CheckInSchedules.Add(schedule);
    }

    public void AddPeriod(CheckInPeriod period)
    {
        context.CheckInPeriods.Add(period);
    }

    public void AddReminder(CheckInReminder reminder)
    {
        context.CheckInReminders.Add(reminder);
    }
}
