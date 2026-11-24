using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Luật khóa kho và tự đóng biên bản chưa khóa (HandoverRecordRules), không cần database.</summary>
public sealed class HandoverVaultLockRulesTests
{
    private static HandoverRecord DraftWith(params HandoverClaimStatus[] claimStatuses)
    {
        HandoverRecord record = new HandoverRecord();
        record.Status = HandoverRecordStatus.Drafting;
        foreach (HandoverClaimStatus status in claimStatuses)
        {
            HandoverClaim claim = new HandoverClaim();
            claim.BeneficiaryId = Guid.NewGuid();
            claim.Status = status;
            record.Claims.Add(claim);
        }
        return record;
    }

    [Theory]
    [InlineData(PlanStatus.Draft, false)]
    [InlineData(PlanStatus.Active, false)]
    [InlineData(PlanStatus.HandoverRequested, true)]
    [InlineData(PlanStatus.HandoverInProgress, true)]
    [InlineData(PlanStatus.Closed, true)]
    public void IsVaultLocked_OnlyFromHandoverRequested(PlanStatus status, bool expected)
    {
        // Chuẩn bị, Hành động
        bool locked = HandoverRecordRules.IsVaultLocked(status);

        // Kiểm tra
        Assert.Equal(expected, locked);
    }

    [Fact]
    public void LockVaultOnAccept_LocksAnActivePlanOnce()
    {
        // Chuẩn bị
        HandoverPlan plan = new HandoverPlan();
        plan.Status = PlanStatus.Active;
        DateTimeOffset now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

        // Hành động
        bool first = HandoverRecordRules.LockVaultOnAccept(plan, now);
        bool second = HandoverRecordRules.LockVaultOnAccept(plan, now.AddMinutes(1));

        // Kiểm tra
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(PlanStatus.HandoverRequested, plan.Status);
        Assert.Equal(now, plan.UpdatedAt);
    }

    [Fact]
    public void ShouldCloseAfterAllRejected_OnlyWhenUnlockedAndNothingIsWaiting()
    {
        // Chuẩn bị
        HandoverRecord allRejected = DraftWith(HandoverClaimStatus.Rejected, HandoverClaimStatus.Rejected);
        HandoverRecord onePending = DraftWith(HandoverClaimStatus.Rejected, HandoverClaimStatus.SupplementRequested);

        // Hành động, Kiểm tra
        Assert.True(HandoverRecordRules.ShouldCloseAfterAllRejected(allRejected, PlanStatus.Active, 0));
        Assert.False(HandoverRecordRules.ShouldCloseAfterAllRejected(allRejected, PlanStatus.Active, 1));
        Assert.False(HandoverRecordRules.ShouldCloseAfterAllRejected(allRejected, PlanStatus.HandoverRequested, 0));
        Assert.False(HandoverRecordRules.ShouldCloseAfterAllRejected(onePending, PlanStatus.Active, 0));
    }

    [Fact]
    public void CloseAfterAllRejected_CancelsWithReason()
    {
        // Chuẩn bị
        HandoverRecord record = DraftWith(HandoverClaimStatus.Rejected);
        DateTimeOffset now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

        // Hành động
        HandoverRecordRules.CloseAfterAllRejected(record, now);

        // Kiểm tra
        Assert.Equal(HandoverRecordStatus.Cancelled, record.Status);
        Assert.Equal(now, record.CancelledAt);
        Assert.Equal(HandoverRecordRules.AllRejectedReason, record.CancelReason);
    }
}
