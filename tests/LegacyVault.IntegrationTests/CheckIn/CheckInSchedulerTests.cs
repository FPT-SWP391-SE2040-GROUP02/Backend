using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Mở/dừng lịch điểm danh (ICheckInScheduler) bằng repository giả.</summary>
public sealed class CheckInSchedulerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly CheckInScheduler scheduler;
    private readonly HandoverPlan plan;

    public CheckInSchedulerTests()
    {
        scheduler = new CheckInScheduler(repository, new FixedTimeProvider(Now));
        plan = CheckInTestData.CreateVault(Guid.NewGuid(), PlanStatus.Active).Plan;
    }

    [Fact]
    public async Task StartFirstPeriod_CreatesSequenceOnePeriodWithDefaults()
    {
        await scheduler.StartFirstPeriodAsync(plan, CancellationToken.None);

        CheckInPeriod period = Assert.Single(repository.Periods);
        Assert.Equal(1, period.Sequence);
        Assert.Equal(Now, period.StartedAt);
        Assert.Equal(Now.AddDays(30), period.DueAt);
        Assert.Equal(Now.AddDays(37), period.GraceEndsAt);
        Assert.Null(period.ClosedAt);

        CheckInSchedule schedule = Assert.Single(repository.Schedules);
        Assert.Equal(plan.Id, schedule.PlanId);
        Assert.Equal(30, schedule.CycleDays);
        Assert.Equal(7, schedule.GraceDays);
    }

    [Fact]
    public async Task StartFirstPeriod_UsesScheduleSettings()
    {
        CheckInSchedule existing = new CheckInSchedule();
        existing.PlanId = plan.Id;
        existing.CycleDays = 60;
        existing.GraceDays = 14;
        repository.Schedules.Add(existing);

        await scheduler.StartFirstPeriodAsync(plan, CancellationToken.None);

        CheckInPeriod period = Assert.Single(repository.Periods);
        Assert.Equal(Now.AddDays(60), period.DueAt);
        Assert.Equal(Now.AddDays(74), period.GraceEndsAt);
        Assert.Single(repository.Schedules);
    }

    [Fact]
    public async Task StartFirstPeriod_CalledTwice_KeepsSingleOpenPeriod()
    {
        await scheduler.StartFirstPeriodAsync(plan, CancellationToken.None);
        await scheduler.StartFirstPeriodAsync(plan, CancellationToken.None);

        Assert.Single(repository.Periods);
    }

    [Fact]
    public async Task Stop_ClosesOpenPeriodWithoutMarkingCheckIn()
    {
        await scheduler.StartFirstPeriodAsync(plan, CancellationToken.None);

        await scheduler.StopAsync(plan, CancellationToken.None);

        CheckInPeriod period = Assert.Single(repository.Periods);
        Assert.Equal(Now, period.ClosedAt);
        Assert.Null(period.CheckedInAt);
    }

    [Fact]
    public async Task Stop_WithoutOpenPeriod_DoesNothing()
    {
        await scheduler.StopAsync(plan, CancellationToken.None);

        Assert.Empty(repository.Periods);
    }
}
