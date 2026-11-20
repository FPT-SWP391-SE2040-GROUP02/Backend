using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests;

public sealed class DevToolsRulesTests
{
    [Fact]
    public void ClockState_FollowsLatestClockAction()
    {
        // Kiểm tra
        Assert.Equal(DevToolsRules.ClockRealTime, DevToolsRules.GetClockState(null));
        Assert.Equal(DevToolsRules.ClockShifted, DevToolsRules.GetClockState(AuditAction.DevTimeTravel));
        Assert.Equal(DevToolsRules.ClockRealTime, DevToolsRules.GetClockState(AuditAction.DevClockReset));
    }

    [Fact]
    public void IsKnownMilestone_AcceptsOnlyRemainingMilestones()
    {
        // Kiểm tra: mốc điểm danh đã bỏ cùng tính năng điểm danh.
        Assert.True(DevToolsRules.IsKnownMilestone(DevToolsRules.ServiceExpired));
        Assert.True(DevToolsRules.IsKnownMilestone(DevToolsRules.RecipientDecisionEnded));
        Assert.False(DevToolsRules.IsKnownMilestone("CHECKIN_OVERDUE"));
        Assert.False(DevToolsRules.IsKnownMilestone("DUE_REMINDER_1"));
        Assert.False(DevToolsRules.IsKnownMilestone("TOMORROW"));
    }

    [Fact]
    public void AllMilestones_ListsServiceThenRecipientDecision()
    {
        // Hành động
        List<string> keys = DevToolsRules.AllMilestones();

        // Kiểm tra
        Assert.Equal(new List<string> { "SERVICE_EXPIRED", "RECIPIENT_DECISION_ENDED" }, keys);
    }
}
