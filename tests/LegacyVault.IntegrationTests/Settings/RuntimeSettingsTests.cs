using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Settings;

/// <summary>Ảnh chụp tham số Admin: chỉ dùng khóa thử riêng để không ảnh hưởng các test khác chạy song song.</summary>
public sealed class RuntimeSettingsTests
{
    [Fact]
    public void Int_ReturnsAdminValue_AndFallsBackWhenMissingOrInvalid()
    {
        RuntimeSettings.Replace(new Dictionary<string, string>
        {
            ["Test.Only.Good"] = "48",
            ["Test.Only.Zero"] = "0",
            ["Test.Only.Text"] = "abc",
        });
        try
        {
            Assert.Equal(48, RuntimeSettings.Int("Test.Only.Good", 72));
            Assert.Equal(72, RuntimeSettings.Int("Test.Only.Zero", 72));
            Assert.Equal(72, RuntimeSettings.Int("Test.Only.Text", 72));
            Assert.Equal(72, RuntimeSettings.Int("Test.Only.Missing", 72));
            Assert.Equal(0, RuntimeSettings.NonNegativeInt("Test.Only.Zero", 5));
            Assert.Equal(TimeSpan.FromHours(48), RuntimeSettings.Hours("Test.Only.Good", 72));
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }
    }

    [Fact]
    public void DemoMode_TurnsDaysHoursAndMinutesIntoSeconds()
    {
        RuntimeSettings.Replace(new Dictionary<string, string> { [TimeUnits.DemoSettingKey] = "true", ["Test.Only.Good"] = "48" });
        try
        {
            Assert.Equal(TimeSpan.FromSeconds(3), TimeUnits.Days(3));
            Assert.Equal(TimeSpan.FromSeconds(48), RuntimeSettings.Hours("Test.Only.Good", 72));
            Assert.Equal(TimeSpan.FromSeconds(30), CheckInTimeline.ComputeDueAt(DateTimeOffset.UnixEpoch, 30) - DateTimeOffset.UnixEpoch);
            Assert.True(CheckInRules.HasCheckedInToday(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            Assert.False(CheckInRules.HasCheckedInToday(DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow));
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }
        Assert.Equal(TimeSpan.FromDays(3), TimeUnits.Days(3));
    }
}
