using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.DemoClocks;

/// <summary>Đồng hồ DEMO và luật thuần của nút "Tua thời gian" (không cần DB).</summary>
public sealed class DemoClockTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DemoClock_NoOffset_FollowsRealTime()
    {
        // Chuẩn bị
        DemoClock clock = new DemoClock();

        // Hành động
        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset reading = clock.GetUtcNow();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Kiểm tra
        Assert.InRange(reading, before, after);
        Assert.Equal(TimeSpan.Zero, clock.Offset);
        Assert.True(clock.IsEnabled);
    }

    [Fact]
    public void DemoClock_WithOffset_ReturnsRealTimePlusOffset()
    {
        // Chuẩn bị
        DemoClock clock = new DemoClock();

        // Hành động
        clock.SetOffset(TimeSpan.FromDays(30));
        DateTimeOffset reading = clock.GetUtcNow();

        // Kiểm tra
        TimeSpan ahead = reading - DateTimeOffset.UtcNow;
        Assert.InRange(ahead, TimeSpan.FromDays(30) - TimeSpan.FromSeconds(5), TimeSpan.FromDays(30) + TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromDays(30), clock.Offset);
    }

    [Fact]
    public async Task DemoClock_SetOffsetFromManyThreads_KeepsOneOfTheValues()
    {
        // Chuẩn bị
        DemoClock clock = new DemoClock();
        List<Task> tasks = new List<Task>();

        // Hành động
        for (int i = 1; i <= 20; i++)
        {
            TimeSpan value = TimeSpan.FromHours(i);
            tasks.Add(Task.Run(() => clock.SetOffset(value)));
        }
        await Task.WhenAll(tasks);

        // Kiểm tra
        Assert.True(clock.Offset >= TimeSpan.FromHours(1) && clock.Offset <= TimeSpan.FromHours(20));
        Assert.Equal(0, clock.Offset.Ticks % TimeSpan.FromHours(1).Ticks);
    }

    [Fact]
    public void DisabledDemoClock_NeverShiftsTimeAndRefusesToSetOffset()
    {
        // Chuẩn bị
        DisabledDemoClock clock = new DisabledDemoClock();

        // Hành động và kiểm tra
        Assert.False(clock.IsEnabled);
        Assert.Equal(TimeSpan.Zero, clock.Offset);
        Assert.Throws<InvalidOperationException>(() => clock.SetOffset(TimeSpan.FromDays(1)));
    }

    [Fact]
    public void TargetInstant_IsNineInTheMorningVietnamTime()
    {
        // Chuẩn bị
        DateOnly date = new DateOnly(2026, 12, 31);

        // Hành động
        DateTimeOffset target = DemoClockRules.TargetInstant(date);

        // Kiểm tra: 9:00 giờ Việt Nam (UTC+7) = 02:00 UTC cùng ngày.
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 2, 0, 0, TimeSpan.Zero), target);
    }

    [Theory]
    [InlineData("2026-12-31", true)]
    [InlineData(" 2026-12-31 ", true)]
    [InlineData("31/12/2026", false)]
    [InlineData("2026-13-01", false)]
    [InlineData("", false)]
    public void TryParseDate_AcceptsOnlyIsoDates(string text, bool expected)
    {
        // Hành động
        DateOnly date;
        bool parsed = DemoClockRules.TryParseDate(text, out date);

        // Kiểm tra
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void FindTravelError_TargetNotAfterNow_ReturnsMessage()
    {
        // Hành động
        string? sameInstant = DemoClockRules.FindTravelError(Now, Now);
        string? past = DemoClockRules.FindTravelError(Now, Now.AddDays(-1));

        // Kiểm tra
        Assert.NotNull(sameInstant);
        Assert.NotNull(past);
    }

    [Fact]
    public void FindTravelError_OverFourHundredDays_ReturnsMessage()
    {
        // Hành động
        string? exactly = DemoClockRules.FindTravelError(Now, Now.AddDays(DemoClockRules.MaxTravelDays));
        string? tooFar = DemoClockRules.FindTravelError(Now, Now.AddDays(DemoClockRules.MaxTravelDays).AddSeconds(1));

        // Kiểm tra
        Assert.Null(exactly);
        Assert.NotNull(tooFar);
    }

    [Fact]
    public void FindTravelError_FutureWithinLimit_ReturnsNull()
    {
        // Hành động
        string? error = DemoClockRules.FindTravelError(Now, Now.AddDays(30));

        // Kiểm tra
        Assert.Null(error);
    }

    [Fact]
    public void CheckInMilestones_UsesCheckInTimelineAndKeepsOnlyThoseInsideTheRange()
    {
        // Chuẩn bị: hạn sau 20 ngày, 3 thư nhắc trễ cách nhau 3 ngày, hết thời gian chờ sau 9 ngày kể từ hạn.
        DateTimeOffset due = Now.AddDays(20);
        DemoOpenPeriod period = new DemoOpenPeriod();
        period.DueAt = due;
        period.GraceEndsAt = due.AddDays(9);
        period.MaxLateReminders = 3;
        period.LateReminderGapDays = 3;
        List<DemoOpenPeriod> periods = new List<DemoOpenPeriod> { period };

        // Hành động: khoảng tua dừng trước khi hết thời gian chờ.
        List<DemoDeadline> milestones = DemoClockRules.CheckInMilestones(periods, Now, due.AddDays(7));

        // Kiểm tra
        Assert.Contains(milestones, m => m.At == due.AddDays(-7) && m.Kind == DemoClockRules.KindCheckInReminder);
        Assert.Contains(milestones, m => m.At == due.AddDays(-1) && m.Kind == DemoClockRules.KindCheckInReminder);
        Assert.Contains(milestones, m => m.At == due && m.Kind == DemoClockRules.KindCheckInDue);
        Assert.Contains(milestones, m => m.At == due.AddDays(3) && m.Kind == DemoClockRules.KindCheckInReminder);
        Assert.Contains(milestones, m => m.At == due.AddDays(6) && m.Kind == DemoClockRules.KindCheckInReminder);
        // Thư nhắc trễ đầu tiên trùng giờ đến hạn nên chỉ còn một mốc "đến hạn".
        Assert.DoesNotContain(milestones, m => m.At == due && m.Kind == DemoClockRules.KindCheckInReminder);
        Assert.DoesNotContain(milestones, m => m.Kind == DemoClockRules.KindCheckInGraceEnd);
        Assert.DoesNotContain(milestones, m => m.At <= Now);
        Assert.DoesNotContain(milestones, m => m.At >= due.AddDays(7));
    }

    [Fact]
    public void CheckInMilestones_RangeCoversGrace_IncludesGraceEnd()
    {
        // Chuẩn bị
        DateTimeOffset due = Now.AddDays(10);
        DemoOpenPeriod period = new DemoOpenPeriod();
        period.DueAt = due;
        period.GraceEndsAt = due.AddDays(9);
        period.MaxLateReminders = 3;
        period.LateReminderGapDays = 3;

        // Hành động
        List<DemoDeadline> milestones = DemoClockRules.CheckInMilestones(
            new List<DemoOpenPeriod> { period }, Now, due.AddDays(30));

        // Kiểm tra
        Assert.Contains(milestones, m => m.At == due.AddDays(9) && m.Kind == DemoClockRules.KindCheckInGraceEnd);
    }

    [Fact]
    public void PickEarliest_ReturnsSmallestTimeOrNull()
    {
        // Chuẩn bị
        DemoDeadline later = new DemoDeadline();
        later.At = Now.AddDays(5);
        later.Kind = "A";
        DemoDeadline sooner = new DemoDeadline();
        sooner.At = Now.AddDays(2);
        sooner.Kind = "B";

        // Hành động
        DemoDeadline? picked = DemoClockRules.PickEarliest(new List<DemoDeadline> { later, sooner });
        DemoDeadline? none = DemoClockRules.PickEarliest(new List<DemoDeadline>());

        // Kiểm tra
        Assert.NotNull(picked);
        Assert.Equal("B", picked.Kind);
        Assert.Null(none);
    }
}
