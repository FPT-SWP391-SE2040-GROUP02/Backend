using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Luật thuần về hạn Verifier của F2C (ALIVE-03, ALIVE-04, ALIVE-08, TIME-01). Không cần SQL.</summary>
public sealed class ObjectionRulesTests
{
    [Fact]
    public void Alive04_ComputeVerifierDueAt_UsesSelfieDeadlinePlus48h_WhenLater()
    {
        // Chuẩn bị: vừa báo, hạn selfie sau 7 ngày
        DateTimeOffset reportedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset selfieDeadlineAt = reportedAt + ObjectionRules.SelfieWindow;

        // Hành động
        DateTimeOffset dueAt = ObjectionRules.ComputeVerifierDueAt(reportedAt, selfieDeadlineAt);

        // Kiểm tra: hạn = hạn selfie + 48 giờ (9 ngày sau khi báo), không phải 48 giờ sau khi báo
        Assert.Equal(selfieDeadlineAt.AddHours(48), dueAt);
        Assert.Equal(reportedAt.AddDays(9), dueAt);
    }

    [Fact]
    public void Alive08_ComputeVerifierDueAt_UsesStartPlus48h_AfterSelfieWindow()
    {
        // Chuẩn bị: hạn selfie đã qua, job giao lại Verifier ở ngày thứ 8
        DateTimeOffset reportedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset selfieDeadlineAt = reportedAt + ObjectionRules.SelfieWindow;
        DateTimeOffset reassignedAt = reportedAt.AddDays(8);

        // Hành động
        DateTimeOffset dueAt = ObjectionRules.ComputeVerifierDueAt(reassignedAt, selfieDeadlineAt);

        // Kiểm tra: Verifier mới có đủ 48 giờ kể từ lúc được giao
        Assert.Equal(reassignedAt.AddHours(48), dueAt);
    }

    [Fact]
    public void Time01_IsVerifierOverdue_ExactMomentIsOverdue_NullDueIsNot()
    {
        // Chuẩn bị
        DateTimeOffset now = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        DeathCaseObjection dueNow = new DeathCaseObjection();
        dueNow.VerifierDueAt = now;
        DeathCaseObjection dueLater = new DeathCaseObjection();
        dueLater.VerifierDueAt = now.AddSeconds(1);
        DeathCaseObjection noDue = new DeathCaseObjection();
        noDue.VerifierDueAt = null;

        // Hành động
        bool exactMoment = ObjectionRules.IsVerifierOverdue(dueNow, now);
        bool notYet = ObjectionRules.IsVerifierOverdue(dueLater, now);
        bool withoutDue = ObjectionRules.IsVerifierOverdue(noDue, now);

        // Kiểm tra: đúng lúc hết hạn là quá hạn; chưa có hạn thì không tính quá hạn
        Assert.True(exactMoment);
        Assert.False(notYet);
        Assert.False(withoutDue);
    }
}
