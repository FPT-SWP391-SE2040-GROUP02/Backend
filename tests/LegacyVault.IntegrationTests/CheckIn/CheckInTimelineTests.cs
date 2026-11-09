using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Luật thời gian điểm danh (SRS DMS-01, DMS-06): không cần DB.</summary>
public sealed class CheckInTimelineTests
{
    // Kỳ mẫu: chu kỳ 30 ngày, 3 thư nhắc trễ cách nhau 3 ngày (thư gửi ngày 30, 33, 36; báo Người thực thi ngày 39).
    private const int MaxLate = 3;
    private const int Gap = 3;
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotifyAt = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeDueAt_AddsCycleDays()
    {
        Assert.Equal(Due, CheckInTimeline.ComputeDueAt(Start, 30));
    }

    [Fact]
    public void ComputeNotifyExecutorAt_AddsLateReminderSpan()
    {
        Assert.Equal(NotifyAt, CheckInTimeline.ComputeNotifyExecutorAt(Due, MaxLate, Gap));
        Assert.Equal(Due.AddDays(5), CheckInTimeline.ComputeNotifyExecutorAt(Due, 5, 1));
    }

    [Fact]
    public void EffectiveStatus_BoundariesUseGreaterOrEqual()
    {
        Assert.Equal(PlanStatus.Active,
            CheckInTimeline.EffectiveStatus(Due, NotifyAt, Due.AddSeconds(-1)));
        Assert.Equal(PlanStatus.CheckinOverdue,
            CheckInTimeline.EffectiveStatus(Due, NotifyAt, Due));
        Assert.Equal(PlanStatus.CheckinOverdue,
            CheckInTimeline.EffectiveStatus(Due, NotifyAt, NotifyAt.AddSeconds(-1)));
        Assert.Equal(PlanStatus.AwaitingCheck,
            CheckInTimeline.EffectiveStatus(Due, NotifyAt, NotifyAt));
    }

    [Fact]
    public void MilestoneAt_ReturnsBeforeDueAndLateReminders()
    {
        Assert.Equal(Due.AddDays(-7), CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInSevenDays, Due, Gap));
        Assert.Equal(Due.AddDays(-1), CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInOneDay, Due, Gap));
        Assert.Equal(Due, CheckInTimeline.MilestoneAt(CheckInReminderKind.LateReminder1, Due, Gap));
        Assert.Equal(Due.AddDays(3), CheckInTimeline.MilestoneAt(CheckInReminderKind.LateReminder2, Due, Gap));
        Assert.Equal(Due.AddDays(6), CheckInTimeline.MilestoneAt(CheckInReminderKind.LateReminder3, Due, Gap));
    }

    [Theory]
    [InlineData(CheckInReminderKind.GraceStarted)]
    [InlineData(CheckInReminderKind.GraceTwoDaysLeft)]
    public void MilestoneAt_LegacyKindsAreRejected(CheckInReminderKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckInTimeline.MilestoneAt(kind, Due, Gap));
    }

    [Fact]
    public void ActiveKinds_FollowsMaxLateReminders()
    {
        Assert.Equal(
            new[]
            {
                CheckInReminderKind.DueInSevenDays, CheckInReminderKind.DueInOneDay,
                CheckInReminderKind.LateReminder1, CheckInReminderKind.LateReminder2, CheckInReminderKind.LateReminder3,
            },
            CheckInTimeline.ActiveKinds(3).ToArray());
        Assert.Equal(4, CheckInTimeline.ActiveKinds(2).Count);
        Assert.Equal(7, CheckInTimeline.ActiveKinds(5).Count);
    }

    [Fact]
    public void LateReminderKind_MapsBothWays()
    {
        for (int i = 1; i <= CheckInRules.MaxLateRemindersLimit; i++)
        {
            Assert.Equal(i, CheckInTimeline.LateReminderIndex(CheckInTimeline.LateReminderKind(i)));
        }
        Assert.Equal(0, CheckInTimeline.LateReminderIndex(CheckInReminderKind.DueInOneDay));
        Assert.Equal(0, CheckInTimeline.LateReminderIndex(CheckInReminderKind.GraceStarted));
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckInTimeline.LateReminderKind(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckInTimeline.LateReminderKind(6));
    }

    [Fact]
    public void LatestReachedMilestone_ReturnsNullBeforeFirst()
    {
        Assert.Null(CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due.AddDays(-7).AddSeconds(-1)));
    }

    [Fact]
    public void LatestReachedMilestone_PicksNewestReached()
    {
        Assert.Equal(CheckInReminderKind.DueInSevenDays,
            CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due.AddDays(-7)));
        Assert.Equal(CheckInReminderKind.DueInOneDay,
            CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due.AddDays(-1)));
        Assert.Equal(CheckInReminderKind.LateReminder1,
            CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due));
        Assert.Equal(CheckInReminderKind.LateReminder2,
            CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due.AddDays(3)));
        Assert.Equal(CheckInReminderKind.LateReminder3,
            CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due.AddDays(6)));
        // Không bao giờ vượt quá số thư Admin đặt, dù đã qua rất lâu.
        Assert.Equal(CheckInReminderKind.LateReminder3,
            CheckInTimeline.LatestReachedMilestone(Due, MaxLate, Gap, Due.AddDays(60)));
        Assert.Equal(CheckInReminderKind.LateReminder1,
            CheckInTimeline.LatestReachedMilestone(Due, 1, Gap, Due.AddDays(60)));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(90, true)]
    [InlineData(0, false)]
    [InlineData(-30, false)]
    [InlineData(31, false)]
    [InlineData(45, false)]
    [InlineData(120, false)]
    public void Rules_AcceptOnlySpecCycleDays(int days, bool expected)
    {
        Assert.Equal(expected, CheckInRules.IsAllowedCycleDays(days));
    }

    [Fact]
    public void Rules_PlanStatusGroups()
    {
        PlanStatus[] all = Enum.GetValues<PlanStatus>();

        Assert.Equal(
            new[] { PlanStatus.Active, PlanStatus.CheckinOverdue, PlanStatus.AwaitingCheck },
            all.Where(CheckInRules.IsRunning).ToArray());
        Assert.Equal(
            new[] { PlanStatus.DeathCaseOpen, PlanStatus.HandoverInProgress, PlanStatus.Closed, PlanStatus.Blocked },
            all.Where(CheckInRules.IsPaused).ToArray());
        Assert.Equal(
            new[] { PlanStatus.DeathCaseOpen, PlanStatus.HandoverInProgress, PlanStatus.Closed },
            all.Where(CheckInRules.IsSettingsLocked).ToArray());
    }
}
