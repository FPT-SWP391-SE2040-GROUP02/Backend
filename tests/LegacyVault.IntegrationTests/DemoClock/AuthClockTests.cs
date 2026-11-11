using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Services;
using LegacyVault.IntegrationTests.CheckIn;
using Xunit;

namespace LegacyVault.IntegrationTests.DemoClocks;

/// <summary>Đồng hồ đăng nhập: mặc định giờ thật, bật Time.AuthFollowsDemoClock thì theo giờ đã tua (không cần DB).</summary>
[Collection(SettingsCollection.Name)]
public sealed class AuthClockTests : IDisposable
{
    public void Dispose()
    {
        RuntimeSettings.Replace(new Dictionary<string, string>());
    }

    private static AuthClock ClockTraveledThreeDays()
    {
        DemoClock demoClock = new DemoClock();
        demoClock.SetOffset(TimeSpan.FromDays(3));
        return new AuthClock(demoClock);
    }

    [Fact]
    public void SwitchOff_IgnoresTravelAndUsesRealTime()
    {
        // Chuẩn bị: chưa có tham số nào (mặc định tắt), hệ thống đã tua 3 ngày.
        RuntimeSettings.Replace(new Dictionary<string, string>());
        AuthClock clock = ClockTraveledThreeDays();

        // Hành động
        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Kiểm tra
        Assert.False(AuthClock.FollowsDemoClock);
        Assert.InRange(now, before, after);
    }

    [Fact]
    public void SwitchOn_FollowsTraveledTime()
    {
        // Chuẩn bị
        RuntimeSettings.Replace(new Dictionary<string, string> { [AuthClock.FollowDemoSettingKey] = "true" });
        AuthClock clock = ClockTraveledThreeDays();

        // Hành động
        DateTimeOffset before = DateTimeOffset.UtcNow.AddDays(3);
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset after = DateTimeOffset.UtcNow.AddDays(3);

        // Kiểm tra
        Assert.True(AuthClock.FollowsDemoClock);
        Assert.InRange(now, before, after);
    }

    [Fact]
    public void SwitchTurnedOffAgain_BackToRealTime()
    {
        // Chuẩn bị: bật rồi tắt lại (Admin gạt công tắc hai lần).
        AuthClock clock = ClockTraveledThreeDays();
        RuntimeSettings.Replace(new Dictionary<string, string> { [AuthClock.FollowDemoSettingKey] = "true" });
        RuntimeSettings.Replace(new Dictionary<string, string> { [AuthClock.FollowDemoSettingKey] = "false" });

        // Hành động
        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Kiểm tra
        Assert.InRange(now, before, after);
    }
}
