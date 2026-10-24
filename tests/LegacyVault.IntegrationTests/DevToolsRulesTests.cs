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
    public void ObjectionWindow_NoOpenCase_IsNotApplicable()
    {
        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateObjectionWindow(null, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.NotApplicable, result.State);
    }

    [Fact]
    public void ObjectionWindow_NoticeNotDelivered_IsNotApplicable()
    {
        // Chuẩn bị
        DeathCase openCase = new DeathCase();
        openCase.Status = DeathCaseStatus.UnderReview;

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateObjectionWindow(openCase, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.NotApplicable, result.State);
    }

    [Fact]
    public void ObjectionWindow_RunningWindow_IsAvailable()
    {
        // Chuẩn bị
        DeathCase openCase = new DeathCase();
        openCase.Status = DeathCaseStatus.UnderReview;
        openCase.ObjectionEndsAt = Now.AddHours(70);

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateObjectionWindow(openCase, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Available, result.State);
    }

    [Fact]
    public void SelfieDeadline_OpenObjectionWithoutSelfie_IsAvailable()
    {
        // Chuẩn bị
        DeathCaseObjection objection = new DeathCaseObjection();
        objection.Status = ObjectionStatus.Open;
        objection.SelfieDeadlineAt = Now.AddDays(7);

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateSelfieDeadline(objection, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.Available, result.State);
    }

    [Fact]
    public void SelfieDeadline_SelfiePassed_IsNotApplicable()
    {
        // Chuẩn bị
        DeathCaseObjection objection = new DeathCaseObjection();
        objection.Status = ObjectionStatus.Open;
        objection.SelfieDeadlineAt = Now.AddDays(7);
        objection.SelfiePassedAt = Now.AddHours(-1);

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateSelfieDeadline(objection, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.NotApplicable, result.State);
    }

    [Fact]
    public void SelfieDeadline_ConcludedObjection_IsNotApplicable()
    {
        // Chuẩn bị
        DeathCaseObjection objection = new DeathCaseObjection();
        objection.Status = ObjectionStatus.Concluded;
        objection.SelfieDeadlineAt = Now.AddDays(7);

        // Hành động
        DevMilestoneResponse result = DevToolsRules.EvaluateSelfieDeadline(objection, Now);

        // Kiểm tra
        Assert.Equal(DevToolsRules.NotApplicable, result.State);
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
    public void IsKnownMilestone_AcceptsOnlyFourKeys()
    {
        // Kiểm tra
        Assert.True(DevToolsRules.IsKnownMilestone(DevToolsRules.CheckInOverdue));
        Assert.True(DevToolsRules.IsKnownMilestone(DevToolsRules.SelfieDeadlinePassed));
        Assert.False(DevToolsRules.IsKnownMilestone("TOMORROW"));
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
