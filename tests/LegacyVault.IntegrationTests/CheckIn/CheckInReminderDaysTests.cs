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

    private static Dictionary<string, string> FiveDays()
    {
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["CheckIn.DueReminderCount"] = "5";
        settings["CheckIn.FirstReminderDaysBeforeDue"] = "10";
        settings["CheckIn.SecondReminderDaysBeforeDue"] = "7";
        settings["CheckIn.ThirdReminderDaysBeforeDue"] = "5";
        settings["CheckIn.FourthReminderDaysBeforeDue"] = "3";
        settings["CheckIn.FifthReminderDaysBeforeDue"] = "1";
        return settings;
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("5", 5)]
    [InlineData("9", 5)]
    [InlineData("0", 2)]
    [InlineData("abc", 2)]
    public void Rules_DueReminderCount_IsClampedFromOneToFive(string raw, int expected)
    {
        // Hành động
        int actual = 0;
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["CheckIn.DueReminderCount"] = raw;
        WithSettings(settings, () => actual = CheckInRules.DueReminderCount);

        // Kiểm tra
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rules_FiveReminders_ReturnsTheAdminDaysInOrder()
    {
        // Hành động
        List<int> days = new List<int>();
        WithSettings(FiveDays(), () => days.AddRange(CheckInRules.DueReminderDays));

        // Kiểm tra
        Assert.Equal(new[] { 10, 7, 5, 3, 1 }, days);
    }

    [Fact]
    public void Rules_FiveRemindersWithBadOrder_AreForcedIntoStrictlyDecreasingDays()
    {
        // Chuẩn bị: các ô 3 đến 5 để mặc định (1, 1, 1) nên phải được nâng lên để không trùng nhau.
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["CheckIn.DueReminderCount"] = "5";
        List<int> days = new List<int>();

        // Hành động
        WithSettings(settings, () => days.AddRange(CheckInRules.DueReminderDays));

        // Kiểm tra
        Assert.Equal(5, days.Count);
        for (int i = 0; i + 1 < days.Count; i++)
        {
            Assert.True(days[i] > days[i + 1]);
        }
        Assert.Equal(1, days[4]);
    }

    [Fact]
    public void Timeline_FiveReminders_AreOrderedBeforeTheLateOnes()
    {
        // Hành động
        IReadOnlyList<CheckInReminderKind> kinds = CheckInTimeline.ActiveKinds(5, 2);

        // Kiểm tra: năm mốc trước hạn, rồi hai thư nhắc trễ; DueReminder3 đến 5 có giá trị enum lớn hơn thư nhắc trễ nhưng vẫn đứng trước.
        Assert.Equal(
            new[]
            {
                CheckInReminderKind.DueInSevenDays, CheckInReminderKind.DueInOneDay, CheckInReminderKind.DueReminder3,
                CheckInReminderKind.DueReminder4, CheckInReminderKind.DueReminder5,
                CheckInReminderKind.LateReminder1, CheckInReminderKind.LateReminder2,
            },
            kinds);
    }

    [Fact]
    public void Timeline_FiveReminders_LatestReachedFollowsEachDay()
    {
        // Hành động và kiểm tra
        int[] days = { 10, 7, 5, 3, 1 };
        Assert.Null(CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-11), days));
        Assert.Equal(CheckInReminderKind.DueInSevenDays, CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-10), days));
        Assert.Equal(CheckInReminderKind.DueReminder3, CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-5), days));
        Assert.Equal(CheckInReminderKind.DueReminder5, CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due.AddDays(-1), days));
        Assert.Equal(CheckInReminderKind.LateReminder1, CheckInTimeline.LatestReachedMilestone(Due, 3, 3, Due, days));
    }

    [Fact]
    public async Task Job_FiveReminders_SendsOneMailPerMilestoneInTimeOrder()
    {
        // Chuẩn bị
        FakeCheckInRepository repository = new FakeCheckInRepository();
        FakeCheckInReminderSender sender = new FakeCheckInReminderSender();
        FixedTimeProvider clock = new FixedTimeProvider(Start);
        CheckInJob job = new CheckInJob(repository, sender, new FakeUnitOfWork(), clock);
        Vault vault = CheckInTestData.CreateVault(Guid.NewGuid(), PlanStatus.Active);
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, 1, Start, 30);
        repository.Periods.Add(period);

        // Hành động: job chạy tại từng mốc, kể cả mốc thứ ba (giá trị enum lớn hơn thư nhắc trễ) và thư nhắc trễ đầu.
        RuntimeSettings.Replace(FiveDays());
        try
        {
            foreach (int daysBefore in new[] { 10, 7, 5, 3, 1, 0 })
            {
                clock.Now = Due.AddDays(-daysBefore);
                await job.ProcessPeriodAsync(period.Id, CancellationToken.None);
            }
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }

        // Kiểm tra: mỗi mốc đúng một thư, không thư nào bị bỏ qua hay gửi lại.
        Assert.Equal(
            new[]
            {
                CheckInReminderKind.DueInSevenDays, CheckInReminderKind.DueInOneDay, CheckInReminderKind.DueReminder3,
                CheckInReminderKind.DueReminder4, CheckInReminderKind.DueReminder5, CheckInReminderKind.LateReminder1,
            },
            sender.Sent);
    }

    [Fact]
    public void Email_ThirdReminder_SaysItsOwnNumberOfDays()
    {
        // Hành động
        string subject = string.Empty;
        WithSettings(FiveDays(), () =>
        {
            subject = CheckInReminderEmail.Build(
                CheckInReminderKind.DueReminder3, "An", Due, GraceEnds, "https://x.test/check-in").Subject;
        });

        // Kiểm tra
        Assert.Equal("Còn 5 ngày nữa là tới hạn điểm danh LegacyVault", subject);
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
