using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests;

public sealed class DevToolsRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private static CheckInPeriod Period(int dueInDays, int graceEndsInDays)
    {
        CheckInPeriod period = new CheckInPeriod();
        period.DueAt = Now.AddDays(dueInDays);
        period.GraceEndsAt = Now.AddDays(graceEndsInDays);
        return period;
    }

    [Fact]
    public void CheckInOverdue_ActivePlanBeforeDueDate_IsAvailable()
    {
        // Chuẩn bị
        CheckInPeriod period = Period(10, 17);

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateCheckInOverdue(PlanStatus.Active, period, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Available, result.State);
    }

    [Fact]
    public void CheckInOverdue_OverduePlan_IsPassed()
    {
        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateCheckInOverdue(PlanStatus.CheckinOverdue, Period(-1, 6), Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Passed, result.State);
    }

    [Fact]
    public void CheckInOverdue_DraftPlan_IsNotApplicable()
    {
        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateCheckInOverdue(PlanStatus.Draft, null, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.NotApplicable, result.State);
        Assert.NotEqual(string.Empty, result.Note);
    }

    [Fact]
    public void GraceEnded_OverduePlanStillInGrace_IsAvailable()
    {
        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateGraceEnded(PlanStatus.CheckinOverdue, Period(-1, 6), Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Available, result.State);
    }

    [Fact]
    public void GraceEnded_AwaitingCheck_IsPassed()
    {
        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateGraceEnded(PlanStatus.AwaitingCheck, Period(-10, -1), Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Passed, result.State);
    }

    [Fact]
    public void CheckInState_ActiveBeforeDue_IsOnTime()
    {
        // Hành động
        string state = DevToolsRules.GetCheckInState(PlanStatus.Active, Period(10, 17), Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.CheckInOnTime, state);
    }

    [Fact]
    public void CheckInState_AwaitingCheck_IsOverdue()
    {
        // Hành động
        string state = DevToolsRules.GetCheckInState(PlanStatus.AwaitingCheck, Period(-10, -1), Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.CheckInOverdueState, state);
    }

    [Fact]
    public void CheckInState_DeathCaseOpen_IsNotRunning()
    {
        // Hành động
        string state = DevToolsRules.GetCheckInState(PlanStatus.DeathCaseOpen, null, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.CheckInNotRunning, state);
    }

    [Fact]
    public void ClockState_FollowsLatestClockAction()
    {
        // Kiểm tra
        Assert.Equal(DevToolsRules.ClockRealTime, DevToolsRules.GetClockState(null));
        Assert.Equal(DevToolsRules.ClockShifted, DevToolsRules.GetClockState(AuditAction.DevTimeTravel));
        Assert.Equal(DevToolsRules.ClockRealTime, DevToolsRules.GetClockState(AuditAction.DevClockReset));
    }

    [Fact]
    public void IsKnownMilestone_AcceptsReminderKeysWithinLimits()
    {
        // Kiểm tra
        Assert.True(DevToolsRules.IsKnownMilestone(DevToolsRules.CheckInOverdue));
        Assert.True(DevToolsRules.IsKnownMilestone("DUE_REMINDER_1"));
        Assert.True(DevToolsRules.IsKnownMilestone("LATE_REMINDER_5"));
        Assert.False(DevToolsRules.IsKnownMilestone("LATE_REMINDER_1"));
        Assert.False(DevToolsRules.IsKnownMilestone("DUE_REMINDER_6"));
        Assert.False(DevToolsRules.IsKnownMilestone("TOMORROW"));
    }

    [Fact]
    public void AllMilestones_FollowsReminderCounts()
    {
        // Hành động
        List<string> keys = DevToolsRules.AllMilestones(2, 3);

        // Kiểm tra
        Assert.Equal(new List<string>
        {
            "DUE_REMINDER_1", "DUE_REMINDER_2", "CHECKIN_OVERDUE", "LATE_REMINDER_2", "LATE_REMINDER_3",
            "GRACE_ENDED", "SERVICE_EXPIRED", "RECIPIENT_DECISION_ENDED",
        }, keys);
    }

    [Fact]
    public void Reminder_DueReminderAhead_IsAvailableWithDays()
    {
        // Chuẩn bị: hạn còn 10 ngày, thư trước hạn 7 ngày chưa tới.
        CheckInPeriod period = Period(10, 19);

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateReminder(
            "DUE_REMINDER_1", PlanStatus.Active, period, Now, new int[] { 7, 1 });

        // Kiểm tra
        Assert.Equal(DevToolsRules.Available, result.State);
        Assert.Equal(7, result.DaysBeforeDue);
    }

    [Fact]
    public void Reminder_DueReminderWhenOverdue_IsPassed()
    {
        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateReminder(
            "DUE_REMINDER_2", PlanStatus.CheckinOverdue, Period(-1, 8), Now, new int[] { 7, 1 });

        // Kiểm tra
        Assert.Equal(DevToolsRules.Passed, result.State);
    }

    [Fact]
    public void Reminder_SecondLateReminderAfterGap_IsPassed()
    {
        // Chuẩn bị: quá hạn 4 ngày, thư nhắc trễ cách nhau 3 ngày nên lần 2 đã tới, lần 3 chưa.
        CheckInPeriod period = Period(-4, 5);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Passed, DevToolsRules.EvaluateReminder(
            "LATE_REMINDER_2", PlanStatus.CheckinOverdue, period, Now, new int[] { 7, 1 }).State);
        Assert.Equal(DevToolsRules.Available, DevToolsRules.EvaluateReminder(
            "LATE_REMINDER_3", PlanStatus.CheckinOverdue, period, Now, new int[] { 7, 1 }).State);
    }

    [Fact]
    public void ShiftToReach_MovesMilestoneJustBeforeNow()
    {
        // Hành động
        TimeSpan shift = DevToolsRules.ShiftToReach(Now.AddDays(3), Now);

        // Kiểm tra
        Assert.Equal(Now - DevToolsRules.PastMargin, Now.AddDays(3) - shift);
        Assert.Equal(TimeSpan.Zero, DevToolsRules.ShiftToReach(Now.AddDays(-1), Now));
    }

    [Fact]
    public void LastCheckInAfterShiftedDue_CheckedInToday_MovesBackBeforeShiftedDue()
    {
        // Chuẩn bị: Owner vừa điểm danh hôm nay, Admin tua hạn về quá khứ.
        DateTimeOffset shiftedDue = Now.AddMinutes(-1);

        // Hành động
        DateTimeOffset? result = DevToolsRules.LastCheckInAfterShiftedDue(Now.AddHours(-2), shiftedDue, 30);

        // Kiểm tra: lùi đúng một chu kỳ trước hạn mới, không còn cùng ngày với hiện tại.
        Assert.Equal(shiftedDue.AddDays(-30), result);
        Assert.False(CheckInRules.HasCheckedInToday(result, Now));
    }

    [Fact]
    public void LastCheckInAfterShiftedDue_AlreadyOlderThanCycle_KeepsValue()
    {
        // Chuẩn bị
        DateTimeOffset oldCheckIn = Now.AddDays(-90);

        // Hành động
        DateTimeOffset? result = DevToolsRules.LastCheckInAfterShiftedDue(oldCheckIn, Now.AddMinutes(-1), 30);

        // Kiểm tra: chỉ lùi, không bao giờ tiến.
        Assert.Equal(oldCheckIn, result);
    }

    [Fact]
    public void LastCheckInAfterShiftedDue_NeverCheckedIn_StaysNull()
    {
        // Hành động và kiểm tra
        Assert.Null(DevToolsRules.LastCheckInAfterShiftedDue(null, Now.AddMinutes(-1), 30));
    }
}
