using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Luật thuần về hạn xét và Verifier khả dụng (ASSIGN-02, ASSIGN-04, TIME-01, TIME-07, D1, D5). Không cần SQL.</summary>
public sealed class DeathCaseAssignmentRulesTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Assign04_DueAtIsFortyEightHoursAfterStartWhenNoObjectionClock()
    {
        // Chuẩn bị: chưa có đồng hồ phản đối (thư báo Owner chưa được giao)

        // Hành động
        DateTimeOffset dueAt = DeathCaseAssignmentRules.ComputeDueAt(Start, null);

        // Kiểm tra
        Assert.Equal(Start.AddHours(48), dueAt);
    }

    [Fact]
    public void Assign04_Time07_DueAtNotBeforeFortyEightHoursAfterObjectionEnds()
    {
        // Chuẩn bị: phản đối còn 60 giờ nữa mới hết, sau mốc start + 48 giờ
        DateTimeOffset objectionEndsAt = Start.AddHours(60);

        // Hành động
        DateTimeOffset dueAt = DeathCaseAssignmentRules.ComputeDueAt(Start, objectionEndsAt);

        // Kiểm tra: hạn = hết phản đối + 48 giờ
        Assert.Equal(objectionEndsAt.AddHours(48), dueAt);
    }

    [Fact]
    public void Assign04_EarlierObjectionEndKeepsStartBasedDueAt()
    {
        // Chuẩn bị: phản đối đã hết từ lâu, hạn tính từ lúc start vẫn muộn hơn
        DateTimeOffset objectionEndsAt = Start.AddHours(-30);

        // Hành động
        DateTimeOffset dueAt = DeathCaseAssignmentRules.ComputeDueAt(Start, objectionEndsAt);

        // Kiểm tra
        Assert.Equal(Start.AddHours(48), dueAt);
    }

    [Fact]
    public void Assign04_ExtendForObjectionNeverShortensDueAt()
    {
        // Chuẩn bị: hạn hiện tại sớm hơn và muộn hơn mốc (hết phản đối + 48 giờ)
        DateTimeOffset objectionEndsAt = Start.AddHours(72);
        DateTimeOffset earlierDue = Start.AddHours(40);
        DateTimeOffset laterDue = Start.AddHours(200);

        // Hành động
        DateTimeOffset extended = DeathCaseAssignmentRules.ExtendForObjection(earlierDue, objectionEndsAt);
        DateTimeOffset unchanged = DeathCaseAssignmentRules.ExtendForObjection(laterDue, objectionEndsAt);

        // Kiểm tra
        Assert.Equal(objectionEndsAt.AddHours(48), extended);
        Assert.Equal(laterDue, unchanged);
    }

    [Fact]
    public void Time01_OverdueExactlyAtDueAt()
    {
        // Chuẩn bị
        DateTimeOffset dueAt = Start.AddHours(48);

        // Hành động
        bool justBefore = DeathCaseAssignmentRules.IsOverdue(dueAt, dueAt.AddTicks(-1));
        bool exactly = DeathCaseAssignmentRules.IsOverdue(dueAt, dueAt);
        bool after = DeathCaseAssignmentRules.IsOverdue(dueAt, dueAt.AddSeconds(1));

        // Kiểm tra
        Assert.False(justBefore);
        Assert.True(exactly);
        Assert.True(after);
    }

    [Fact]
    public void Assign02_InactiveOrDemotedVerifierIsUnavailable()
    {
        // Chuẩn bị
        User active = NewUser(UserRole.Verifier, UserStatus.Active);
        User locked = NewUser(UserRole.Verifier, UserStatus.Locked);
        User disabled = NewUser(UserRole.Verifier, UserStatus.Disabled);
        User demoted = NewUser(UserRole.User, UserStatus.Active);

        // Hành động và kiểm tra
        Assert.True(DeathCaseAssignmentRules.IsVerifierAvailable(active));
        Assert.False(DeathCaseAssignmentRules.IsVerifierAvailable(locked));
        Assert.False(DeathCaseAssignmentRules.IsVerifierAvailable(disabled));
        Assert.False(DeathCaseAssignmentRules.IsVerifierAvailable(demoted));
        Assert.False(DeathCaseAssignmentRules.IsVerifierAvailable(null));
    }

    [Fact]
    public void Time02_ReminderNeededOncePerDueAt()
    {
        // Chuẩn bị: còn 10 giờ tới hạn
        DateTimeOffset dueAt = Start.AddHours(10);
        DeathCaseAssignment assignment = NewAssignment(dueAt, null);

        // Hành động
        bool beforeReminder = DeathCaseAssignmentRules.NeedsReminder(assignment, Start);
        assignment.RemindedDueAt = dueAt;
        bool afterReminder = DeathCaseAssignmentRules.NeedsReminder(assignment, Start);
        bool afterReminderAndOverdue = DeathCaseAssignmentRules.NeedsReminder(assignment, dueAt.AddHours(1));

        // Kiểm tra: nhắc đúng một lần cho mỗi hạn
        Assert.True(beforeReminder);
        Assert.False(afterReminder);
        Assert.False(afterReminderAndOverdue);
    }

    [Fact]
    public void Time02_MovedDueAtNeedsNewReminder()
    {
        // Chuẩn bị: đã nhắc cho hạn cũ, sau đó hạn bị dời tới còn 8 giờ
        DateTimeOffset oldDueAt = Start.AddHours(10);
        DeathCaseAssignment assignment = NewAssignment(oldDueAt, oldDueAt);
        assignment.DueAt = Start.AddHours(8);

        // Hành động
        bool needs = DeathCaseAssignmentRules.NeedsReminder(assignment, Start);

        // Kiểm tra: hạn mới khác hạn đã nhắc nên nhắc lại
        Assert.True(needs);
    }

    [Fact]
    public void Assign04_NoReminderBeforeTwelveHoursLeft()
    {
        // Chuẩn bị: hạn còn 12 giờ + 1 giây, rồi đúng 12 giờ
        DeathCaseAssignment assignment = NewAssignment(Start.AddHours(12).AddSeconds(1), null);

        // Hành động
        bool tooEarly = DeathCaseAssignmentRules.NeedsReminder(assignment, Start);
        assignment.DueAt = Start.AddHours(12);
        bool exactly = DeathCaseAssignmentRules.NeedsReminder(assignment, Start);

        // Kiểm tra
        Assert.False(tooEarly);
        Assert.True(exactly);
    }

    [Fact]
    public void ROLE03_NormalizeEmail_TrimsAndLowercases()
    {
        // Chuẩn bị
        string raw = "  Nguoi.Nhan@Example.COM ";

        // Hành động
        string normalized = DeathCaseAssignmentRules.NormalizeEmail(raw);

        // Kiểm tra
        Assert.Equal("nguoi.nhan@example.com", normalized);
    }

    private static DeathCaseAssignment NewAssignment(DateTimeOffset dueAt, DateTimeOffset? remindedDueAt)
    {
        DeathCaseAssignment assignment = new DeathCaseAssignment();
        assignment.DueAt = dueAt;
        assignment.RemindedDueAt = remindedDueAt;
        return assignment;
    }

    private static User NewUser(UserRole role, UserStatus status)
    {
        User user = new User();
        user.Role = role;
        user.Status = status;
        return user;
    }
}
