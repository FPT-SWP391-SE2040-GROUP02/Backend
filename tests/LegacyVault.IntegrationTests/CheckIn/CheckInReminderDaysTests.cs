using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>
/// Các test đổi cấu hình dùng chung của cả tiến trình (RuntimeSettings) nên chạy riêng, không song song với test khác,
/// để không làm lệch số ngày nhắc của các test điểm danh còn lại.
/// </summary>
[CollectionDefinition(SettingsCollection.Name, DisableParallelization = true)]
public sealed class SettingsCollection
{
    public const string Name = "RuntimeSettingsSerial";
}

/// <summary>
/// Số ngày của hai thư nhắc trước hạn do Admin đặt (CheckIn.FirstReminderDaysBeforeDue và
/// CheckIn.SecondReminderDaysBeforeDue). Mặc định vẫn là 7 ngày và 1 ngày.
/// </summary>
[Collection(SettingsCollection.Name)]
public sealed class CheckInReminderDaysTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnds = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Đặt cấu hình Admin trong lúc chạy một đoạn code rồi trả về như cũ (rỗng).</summary>
    private static void WithSettings(Dictionary<string, string> settings, Action action)
    {
        RuntimeSettings.Replace(settings);
        try
        {
            action();
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }
    }

    private static Dictionary<string, string> Days(string first, string second)
    {
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["CheckIn.FirstReminderDaysBeforeDue"] = first;
        settings["CheckIn.SecondReminderDaysBeforeDue"] = second;
        return settings;
    }

    [Fact]
    public void Rules_NoSetting_UseTheOldDefaultsOfSevenAndOneDay()
    {
        // Chuẩn bị và hành động
        int first = 0;
        int second = 0;
        WithSettings(new Dictionary<string, string>(), () =>
        {
            first = CheckInRules.FirstReminderDaysBeforeDue;
            second = CheckInRules.SecondReminderDaysBeforeDue;
        });

        // Kiểm tra
        Assert.Equal(7, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void Rules_AdminSetsThreeAndOne_ReturnsThem()
    {
        // Hành động
        int first = 0;
        int second = 0;
        WithSettings(Days("3", "1"), () =>
        {
            first = CheckInRules.FirstReminderDaysBeforeDue;
            second = CheckInRules.SecondReminderDaysBeforeDue;
        });

        // Kiểm tra
        Assert.Equal(3, first);
        Assert.Equal(1, second);
    }

    [Theory]
    [InlineData("1", "5", 6, 5)]
    [InlineData("5", "5", 6, 5)]
    [InlineData("100", "1", 60, 1)]
    [InlineData("40", "99", 40, 30)]
    [InlineData("0", "0", 7, 1)]
    [InlineData("abc", "xyz", 7, 1)]
    public void Rules_BadValues_AreForcedIntoAValidOrder(string first, string second, int expectedFirst, int expectedSecond)
    {
        // Hành động
        int actualFirst = 0;
        int actualSecond = 0;
        WithSettings(Days(first, second), () =>
        {
            actualFirst = CheckInRules.FirstReminderDaysBeforeDue;
            actualSecond = CheckInRules.SecondReminderDaysBeforeDue;
        });

        // Kiểm tra: thư thứ nhất luôn xa hạn hơn thư thứ hai, thư thứ hai từ 1 đến 30 ngày, thư thứ nhất tối đa 60 ngày.
        Assert.Equal(expectedFirst, actualFirst);
        Assert.Equal(expectedSecond, actualSecond);
        Assert.True(actualFirst > actualSecond);
    }

    [Fact]
    public void Timeline_CustomDays_PlacesTheTwoReminderBeforeDue()
    {
        // Hành động
        DateTimeOffset firstAt = CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInSevenDays, Due, 3, 3, 1);
        DateTimeOffset secondAt = CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInOneDay, Due, 3, 3, 2);

        // Kiểm tra
        Assert.Equal(Due.AddDays(-3), firstAt);
        Assert.Equal(Due.AddDays(-2), secondAt);
    }

    [Fact]
    public void Timeline_CustomDays_LatestReachedUsesTheNewDays()
    {
        // Hành động và kiểm tra: với 3 ngày thì 5 ngày trước hạn chưa có mốc nào, 3 ngày trước hạn mới tới mốc thứ nhất.
        Assert.Null(CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-5), 3, 1));
        Assert.Equal(CheckInReminderKind.DueInSevenDays,
            CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-3), 3, 1));
        Assert.Equal(CheckInReminderKind.DueInOneDay,
            CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-1), 3, 1));
    }

    [Fact]
    public void Timeline_OldOverloads_StillMeanSevenAndOneDay()
    {
        // Hành động và kiểm tra
        Assert.Equal(Due.AddDays(-7), CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInSevenDays, Due, 3));
        Assert.Equal(Due.AddDays(-1), CheckInTimeline.MilestoneAt(CheckInReminderKind.DueInOneDay, Due, 3));
        Assert.Equal(CheckInReminderKind.DueInSevenDays,
            CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-7)));
    }

    [Fact]
    public async Task Job_AdminSetsThreeDays_FirstReminderIsSentThreeDaysBeforeDueNotSeven()
    {
        // Chuẩn bị
        FakeCheckInRepository repository = new FakeCheckInRepository();
        FakeCheckInReminderSender sender = new FakeCheckInReminderSender();
        FixedTimeProvider clock = new FixedTimeProvider(Start);
        CheckInJob job = new CheckInJob(repository, sender, new FakeUnitOfWork(), clock);
        Vault vault = CheckInTestData.CreateVault(Guid.NewGuid(), PlanStatus.Active);
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, 1, Start, 30);
        repository.Periods.Add(period);
        List<CheckInReminderKind> sentSevenDaysBefore = new List<CheckInReminderKind>();
        List<CheckInReminderKind> sentAtThreeDaysBefore = new List<CheckInReminderKind>();

        // Hành động
        RuntimeSettings.Replace(Days("3", "1"));
        try
        {
            clock.Now = Due.AddDays(-7);
            await job.ProcessPeriodAsync(period.Id, CancellationToken.None);
            sentSevenDaysBefore.AddRange(sender.Sent);

            clock.Now = Due.AddDays(-3);
            await job.ProcessPeriodAsync(period.Id, CancellationToken.None);
            sentAtThreeDaysBefore.AddRange(sender.Sent);
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }

        // Kiểm tra
        Assert.Empty(sentSevenDaysBefore);
        Assert.Equal(new[] { CheckInReminderKind.DueInSevenDays }, sentAtThreeDaysBefore);
    }

    [Fact]
    public void DemoClock_CustomDays_MilestonesFollowTheAdminSetting()
    {
        // Chuẩn bị
        DemoOpenPeriod period = new DemoOpenPeriod();
        period.DueAt = Due;
        period.GraceEndsAt = GraceEnds;
        period.MaxLateReminders = 3;
        period.LateReminderGapDays = 3;
        List<DemoOpenPeriod> periods = new List<DemoOpenPeriod> { period };
        List<DemoDeadline> milestones = new List<DemoDeadline>();

        // Hành động
        WithSettings(Days("3", "1"), () =>
        {
            milestones = DemoClockRules.CheckInMilestones(periods, Start, Due);
        });

        // Kiểm tra: mốc thứ nhất là trước hạn 3 ngày chứ không phải 7 ngày.
        Assert.Contains(milestones, m => m.At == Due.AddDays(-3));
        Assert.Contains(milestones, m => m.At == Due.AddDays(-1));
        Assert.DoesNotContain(milestones, m => m.At == Due.AddDays(-7));
    }

    [Fact]
    public void Email_CustomDays_SubjectsSayTheRealNumberOfDays()
    {
        // Hành động
        string defaultFirst = string.Empty;
        string customFirst = string.Empty;
        string defaultSecond = string.Empty;
        string customSecond = string.Empty;
        WithSettings(new Dictionary<string, string>(), () =>
        {
            defaultFirst = CheckInReminderEmail.Build(
                CheckInReminderKind.DueInSevenDays, "An", Due, GraceEnds, "https://x.test/check-in").Subject;
            defaultSecond = CheckInReminderEmail.Build(
                CheckInReminderKind.DueInOneDay, "An", Due, GraceEnds, "https://x.test/check-in").Subject;
        });
        WithSettings(Days("3", "2"), () =>
        {
            customFirst = CheckInReminderEmail.Build(
                CheckInReminderKind.DueInSevenDays, "An", Due, GraceEnds, "https://x.test/check-in").Subject;
            customSecond = CheckInReminderEmail.Build(
                CheckInReminderKind.DueInOneDay, "An", Due, GraceEnds, "https://x.test/check-in").Subject;
        });

        // Kiểm tra: mặc định giữ nguyên câu cũ.
        Assert.Equal("Còn 7 ngày nữa là tới hạn điểm danh LegacyVault", defaultFirst);
        Assert.Equal("Ngày mai là hạn điểm danh LegacyVault", defaultSecond);
        Assert.Equal("Còn 3 ngày nữa là tới hạn điểm danh LegacyVault", customFirst);
        Assert.Equal("Còn 2 ngày nữa là tới hạn điểm danh LegacyVault", customSecond);
    }
}
