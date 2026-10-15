using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

public sealed class CheckInScheduler : ICheckInScheduler
{
    private readonly ICheckInRepository repository;
    private readonly TimeProvider timeProvider;

    public CheckInScheduler(ICheckInRepository repository, TimeProvider timeProvider)
    {
        this.repository = repository;
        this.timeProvider = timeProvider;
    }

    public async Task StartFirstPeriodAsync(HandoverPlan plan, CancellationToken ct)
    {
        CheckInPeriod? openPeriod = await repository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (openPeriod != null)
        {
            // SETUP-14: kích hoạt lặp chỉ có một kỳ.
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        CheckInSchedule? schedule = await repository.FindScheduleByPlanIdAsync(plan.Id, ct);
        if (schedule == null)
        {
            schedule = new CheckInSchedule();
            schedule.PlanId = plan.Id;
            schedule.CycleDays = CheckInRules.DefaultCycleDays;
            schedule.GraceDays = CheckInRules.DefaultGraceDays;
            schedule.CreatedAt = now;
            repository.AddSchedule(schedule);
        }

        CheckInRules.ApplyPendingSettings(schedule, now);
        int lastSequence = await repository.GetMaxSequenceAsync(plan.Id, ct);
        CheckInPeriod period = new CheckInPeriod();
        period.PlanId = plan.Id;
        period.Sequence = lastSequence + 1;
        period.StartedAt = now;
        period.DueAt = CheckInTimeline.ComputeDueAt(now, schedule.CycleDays);
        period.GraceEndsAt = CheckInTimeline.ComputeGraceEndsAt(period.DueAt, schedule.GraceDays);
        repository.AddPeriod(period);
    }

    public async Task StopAsync(HandoverPlan plan, CancellationToken ct)
    {
        CheckInPeriod? openPeriod = await repository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (openPeriod == null)
        {
            return;
        }
        openPeriod.ClosedAt = timeProvider.GetUtcNow();
    }
}
