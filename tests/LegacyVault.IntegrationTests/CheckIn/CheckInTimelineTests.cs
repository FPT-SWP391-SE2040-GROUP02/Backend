using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Luật thời gian điểm danh (SRS DMS-01, DMS-06): không cần DB.</summary>
public sealed class CheckInTimelineTests
{
    // Kỳ mẫu: chu kỳ 30 ngày, chờ 7 ngày.
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnds = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeDueAt_AddsCycleDays()
    {
        Assert.Equal(Due, CheckInTimeline.ComputeDueAt(Start, 30));
    }

    [Fact]
    public void ComputeGraceEndsAt_AddsGraceDays()
    {
        Assert.Equal(GraceEnds, CheckInTimeline.ComputeGraceEndsAt(Due, 7));
    }

    [Fact]
    public void EffectiveStatus_BoundariesUseGreaterOrEqual()
    {
        Assert.Equal(PlanStatus.Active,
            CheckInTimeline.EffectiveStatus(Due, GraceEnds, Due.AddSeconds(-1)));
        Assert.Equal(PlanStatus.CheckinOverdue,
            CheckInTimeline.EffectiveStatus(Due, GraceEnds, Due));
        Assert.Equal(PlanStatus.CheckinOverdue,
            CheckInTimeline.EffectiveStatus(Due, GraceEnds, GraceEnds.AddSeconds(-1)));
        Assert.Equal(PlanStatus.AwaitingCheck,
            CheckInTimeline.EffectiveStatus(Due, GraceEnds, GraceEnds));
    }

    [Fact]
    public void MilestoneAt_ReturnsFourSpecMilestones()
    {
        Assert.Equal(Due.AddDays(-7), CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInSevenDays, Due, GraceEnds));
        Assert.Equal(Due.AddDays(-1), CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInOneDay, Due, GraceEnds));
        Assert.Equal(Due, CheckInTimeline.MilestoneAt(CheckInReminderKind.GraceStarted, Due, GraceEnds));
        Assert.Equal(GraceEnds.AddDays(-2), CheckInTimeline.MilestoneAt(CheckInReminderKind.GraceTwoDaysLeft, Due, GraceEnds));
    }

    [Fact]
    public void LatestReachedMilestone_ReturnsNullBeforeFirst()
    {
        Assert.Null(CheckInTimeline.LatestReachedMilestone(Due, GraceEnds, Due.AddDays(-7).AddSeconds(-1)));
    }

    [Fact]
    public void LatestReachedMilestone_PicksNewestReached()
    {
        Assert.Equal(CheckInReminderKind.DueInSevenDays,
            CheckInTimeline.LatestReachedMilestone(Due, GraceEnds, Due.AddDays(-7)));
        Assert.Equal(CheckInReminderKind.DueInOneDay,
            CheckInTimeline.LatestReachedMilestone(Due, GraceEnds, Due.AddDays(-1)));
        Assert.Equal(CheckInReminderKind.GraceStarted,
            CheckInTimeline.LatestReachedMilestone(Due, GraceEnds, Due));
        Assert.Equal(CheckInReminderKind.GraceTwoDaysLeft,
            CheckInTimeline.LatestReachedMilestone(Due, GraceEnds, GraceEnds.AddDays(-2)));
        Assert.Equal(CheckInReminderKind.GraceTwoDaysLeft,
            CheckInTimeline.LatestReachedMilestone(Due, GraceEnds, GraceEnds.AddDays(30)));
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

    [Theory]
    [InlineData(7, true)]
    [InlineData(14, true)]
    [InlineData(30, true)]
    [InlineData(0, false)]
    [InlineData(-7, false)]
    [InlineData(8, false)]
    [InlineData(21, false)]
    [InlineData(60, false)]
    public void Rules_AcceptOnlySpecGraceDays(int days, bool expected)
    {
        Assert.Equal(expected, CheckInRules.IsAllowedGraceDays(days));
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
