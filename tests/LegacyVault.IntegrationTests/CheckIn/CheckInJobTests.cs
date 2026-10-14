using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Job chuyển trạng thái và gửi nhắc (CheckInJob) bằng repository giả.</summary>
public sealed class CheckInJobTests
{
    // Kỳ mẫu: chu kỳ 30 ngày, chờ 7 ngày.
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnds = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly FakeCheckInReminderSender sender = new FakeCheckInReminderSender();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Start);
    private readonly CheckInJob job;

    public CheckInJobTests()
    {
        job = new CheckInJob(repository, sender, unitOfWork, clock);
    }

    private CheckInPeriod SeedPeriod(PlanStatus planStatus)
    {
        Vault vault = CheckInTestData.CreateVault(Guid.NewGuid(), planStatus);
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, 1, Start, 30, 7);
        repository.Periods.Add(period);
        return period;
    }

    private async Task RunAtAsync(CheckInPeriod period, DateTimeOffset now)
    {
        clock.Now = now;
        await job.ProcessPeriodAsync(period.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Process_BeforeDue_DoesNotChangeStatus()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due.AddSeconds(-1));

        Assert.Equal(PlanStatus.Active, period.Plan.Status);
        Assert.Null(period.OverdueAt);
    }

    [Fact]
    public async Task Process_AtDue_MovesToOverdueOnceAndSendsGraceStarted()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due);

        Assert.Equal(PlanStatus.CheckinOverdue, period.Plan.Status);
        Assert.Equal(Due, period.OverdueAt);
        Assert.Equal(new[] { CheckInReminderKind.GraceStarted }, sender.Sent);
    }

    [Fact]
    public async Task Process_AtGraceEnd_MovesToAwaitingCheckAndStopsReminders()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        period.OverdueAt = Due;

        await RunAtAsync(period, GraceEnds);

        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
        Assert.Equal(GraceEnds, period.AwaitingCheckAt);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Process_LongDowntime_JumpsToAwaitingCheckInOneRun()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, GraceEnds.AddDays(10));

        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
        Assert.NotNull(period.OverdueAt);
        Assert.NotNull(period.AwaitingCheckAt);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Process_RunTwice_DoesNotDuplicateWorkOrReminders()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due);
        DateTimeOffset? overdueAfterFirst = period.OverdueAt;
        await RunAtAsync(period, Due);

        Assert.Single(sender.Sent);
        Assert.Single(period.Reminders, r => r.Kind == CheckInReminderKind.GraceStarted);
        Assert.Equal(overdueAfterFirst, period.OverdueAt);
        Assert.Null(period.AwaitingCheckAt);
    }

    [Fact]
    public async Task Process_SendsEachFourMilestoneOnce()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due.AddDays(-7));
        await RunAtAsync(period, Due.AddDays(-1));
        await RunAtAsync(period, Due);
        await RunAtAsync(period, GraceEnds.AddDays(-2));
        await RunAtAsync(period, GraceEnds.AddDays(-2).AddHours(1));

        Assert.Equal(
            new[]
            {
                CheckInReminderKind.DueInSevenDays,
                CheckInReminderKind.DueInOneDay,
                CheckInReminderKind.GraceStarted,
                CheckInReminderKind.GraceTwoDaysLeft,
            },
            sender.Sent);
    }

    [Fact]
    public async Task Process_AfterDowntime_SendsOnlyLatestMilestoneAndSkipsOlder()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due.AddDays(-1).AddHours(1));

        Assert.Equal(new[] { CheckInReminderKind.DueInOneDay }, sender.Sent);
        CheckInReminder skipped = Assert.Single(period.Reminders, r => r.Kind == CheckInReminderKind.DueInSevenDays);
        Assert.Equal(CheckInReminderStatus.Skipped, skipped.Status);
    }

    [Fact]
    public async Task Process_SenderFails_KeepsPendingAndRetriesWithoutDuplicate()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);
        sender.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAtAsync(period, Due.AddDays(-7)));
        CheckInReminder pending = Assert.Single(period.Reminders);
        Assert.Equal(CheckInReminderStatus.Pending, pending.Status);
        Assert.Empty(sender.Sent);

        await RunAtAsync(period, Due.AddDays(-7).AddMinutes(1));

        Assert.Single(period.Reminders, r => r.Kind == CheckInReminderKind.DueInSevenDays);
        Assert.Equal(CheckInReminderStatus.Sent, pending.Status);
        Assert.NotNull(pending.SentAt);
        Assert.Equal(new[] { CheckInReminderKind.DueInSevenDays }, sender.Sent);
    }

    [Theory]
    [InlineData(PlanStatus.DeathCaseOpen)]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.HandoverInProgress)]
    public async Task Process_LockedOrDraftPlan_IsIgnored(PlanStatus status)
    {
        CheckInPeriod period = SeedPeriod(status);

        await RunAtAsync(period, GraceEnds.AddDays(1));

        Assert.Equal(status, period.Plan.Status);
        Assert.Null(period.OverdueAt);
        Assert.Empty(sender.Sent);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Process_ClosedPeriod_IsIgnored()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);
        period.ClosedAt = Start;
        period.CheckedInAt = Start;

        await RunAtAsync(period, GraceEnds.AddDays(1));

        Assert.Equal(PlanStatus.Active, period.Plan.Status);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Process_UnknownPeriodId_DoesNothing()
    {
        await job.ProcessPeriodAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task FindOpenPeriodIds_OnlyReturnsPeriodsNeedingWork()
    {
        CheckInPeriod far = SeedPeriod(PlanStatus.Active);
        CheckInPeriod near = SeedPeriod(PlanStatus.Active);
        near.DueAt = Start.AddDays(7);
        far.DueAt = Start.AddDays(30);
        clock.Now = Start;

        IReadOnlyList<Guid> ids = await job.FindOpenPeriodIdsAsync(CancellationToken.None);

        Assert.Equal(new[] { near.Id }, ids);
    }
}
