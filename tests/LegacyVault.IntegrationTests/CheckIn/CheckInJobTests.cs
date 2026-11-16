using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Job chuyển trạng thái và gửi nhắc (CheckInJob) bằng repository giả.</summary>
public sealed class CheckInJobTests
{
    // Kỳ mẫu: chu kỳ 30 ngày, chờ 7 ngày.
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnds = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly FakeCheckInReminderSender sender = new FakeCheckInReminderSender();
    private readonly FakeCheckInOverdueNoticeSender overdueSender = new FakeCheckInOverdueNoticeSender();
    private readonly FakeAvailableExecutorReader executorReader = new FakeAvailableExecutorReader();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Start);
    private readonly CheckInJob job;

    public CheckInJobTests()
    {
        job = new CheckInJob(repository, sender, overdueSender, executorReader, users,
            NullLogger<CheckInJob>.Instance, unitOfWork, clock);
    }

    private CheckInPeriod SeedPeriod(PlanStatus planStatus)
    {
        Vault vault = CheckInTestData.CreateVault(Guid.NewGuid(), planStatus);
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, 1, Start, 30);
        repository.Periods.Add(period);
        return period;
    }

    private async Task RunAtAsync(CheckInPeriod period, DateTimeOffset now)
    {
        clock.Now = now;
        await job.ProcessPeriodAsync(period.Id, CancellationToken.None);
    }

    private User AddExecutor(string email, UserStatus status)
    {
        User user = new User();
        user.Email = email;
        user.FullName = "Người thực thi";
        user.Status = status;
        users.Users.Add(user);
        Executor executor = new Executor();
        executor.UserId = user.Id;
        executor.Status = ExecutorStatus.Accepted;
        executorReader.Available.Add(executor);
        return user;
    }

    [Fact]
    public async Task Process_TransitionToAwaitingCheck_NotifiesExecutorExactlyOnce()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        period.OverdueAt = Due;
        AddExecutor("executor@example.com", UserStatus.Active);

        await RunAtAsync(period, GraceEnds);
        await RunAtAsync(period, GraceEnds.AddMinutes(1));
        await RunAtAsync(period, GraceEnds.AddDays(1));

        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
        CheckInOverdueNotice notice = Assert.Single(overdueSender.Sent);
        Assert.Equal("executor@example.com", notice.ToEmail);
        Assert.Equal(period.Plan.VaultId, notice.VaultId);
        Assert.Equal(period.DueAt, notice.DueAt);
    }

    [Fact]
    public async Task Process_BeforeAwaitingCheck_DoesNotNotifyExecutor()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);
        AddExecutor("executor@example.com", UserStatus.Active);

        await RunAtAsync(period, Due);

        Assert.Equal(PlanStatus.CheckinOverdue, period.Plan.Status);
        Assert.Empty(overdueSender.Sent);
    }

    [Fact]
    public async Task Process_NoExecutor_StillMovesStatusAndSendsNothing()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        period.OverdueAt = Due;

        await RunAtAsync(period, GraceEnds);

        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
        Assert.Empty(overdueSender.Sent);
    }

    [Fact]
    public async Task Process_LockedExecutorIsSkipped()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        period.OverdueAt = Due;
        AddExecutor("locked@example.com", UserStatus.Locked);
        AddExecutor("active@example.com", UserStatus.Active);

        await RunAtAsync(period, GraceEnds);

        CheckInOverdueNotice notice = Assert.Single(overdueSender.Sent);
        Assert.Equal("active@example.com", notice.ToEmail);
    }

    [Fact]
    public async Task Process_SendFailure_IsSwallowedAndStatusStaysChanged()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        period.OverdueAt = Due;
        AddExecutor("executor@example.com", UserStatus.Active);
        overdueSender.FailNext = true;

        await RunAtAsync(period, GraceEnds);
        await RunAtAsync(period, GraceEnds.AddMinutes(1));

        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
        Assert.Equal(GraceEnds, period.AwaitingCheckAt);
        Assert.Empty(overdueSender.Sent);
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
    public async Task Process_AtDue_MovesToOverdueOnceAndSendsFirstLateReminder()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due);

        Assert.Equal(PlanStatus.CheckinOverdue, period.Plan.Status);
        Assert.Equal(Due, period.OverdueAt);
        Assert.Equal(new[] { CheckInReminderKind.LateReminder1 }, sender.Sent);
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
        Assert.Single(period.Reminders, r => r.Kind == CheckInReminderKind.LateReminder1);
        Assert.Equal(overdueAfterFirst, period.OverdueAt);
        Assert.Null(period.AwaitingCheckAt);
    }

    [Fact]
    public async Task Process_SendsEachMilestoneOnce()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due.AddDays(-7));
        await RunAtAsync(period, Due.AddDays(-1));
        await RunAtAsync(period, Due);
        await RunAtAsync(period, Due.AddDays(3));
        await RunAtAsync(period, Due.AddDays(6));
        await RunAtAsync(period, Due.AddDays(6).AddHours(1));

        Assert.Equal(
            new[]
            {
                CheckInReminderKind.DueInSevenDays,
                CheckInReminderKind.DueInOneDay,
                CheckInReminderKind.LateReminder1,
                CheckInReminderKind.LateReminder2,
                CheckInReminderKind.LateReminder3,
            },
            sender.Sent);
        Assert.Equal(PlanStatus.CheckinOverdue, period.Plan.Status);
    }

    [Fact]
    public async Task Process_AfterLastLateReminderGap_NotifiesExecutorAndStopsReminders()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.Active);

        await RunAtAsync(period, Due.AddDays(6));
        await RunAtAsync(period, Due.AddDays(9));

        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
        Assert.Equal(Due.AddDays(9), period.AwaitingCheckAt);
        Assert.DoesNotContain(CheckInReminderKind.GraceStarted, sender.Sent);
        Assert.Equal(CheckInReminderKind.LateReminder3, sender.Sent[sender.Sent.Count - 1]);
    }

    [Fact]
    public async Task Process_UsesLateReminderSettingsFrozenInThePeriod()
    {
        // Kỳ này được chốt 2 thư nhắc trễ cách nhau 1 ngày: thư gửi ở ngày 0 và ngày 1, báo Người thực thi ở ngày 2.
        Vault vault = CheckInTestData.CreateVault(Guid.NewGuid(), PlanStatus.Active);
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, 1, Start, 30, maxLateReminders: 2, gapDays: 1);
        repository.Periods.Add(period);

        await RunAtAsync(period, Due);
        await RunAtAsync(period, Due.AddDays(1));
        await RunAtAsync(period, Due.AddDays(2));

        Assert.Equal(new[] { CheckInReminderKind.LateReminder1, CheckInReminderKind.LateReminder2 }, sender.Sent);
        Assert.Equal(PlanStatus.AwaitingCheck, period.Plan.Status);
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
