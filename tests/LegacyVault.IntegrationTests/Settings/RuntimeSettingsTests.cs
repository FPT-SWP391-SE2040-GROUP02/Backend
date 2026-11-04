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
}
