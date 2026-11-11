using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Settings;

/// <summary>Số điện thoại công ty (Company.Phone) phải đúng dạng; để trống thì được (hệ thống dùng số mặc định).</summary>
public sealed class CompanyPhoneRuleTests
{
    private const string Key = "Company.Phone";

    [Theory]
    [InlineData("1900 0000")]
    [InlineData("0901 234 567")]
    [InlineData("090.123.4567")]
    [InlineData("+84 901 234 567")]
    [InlineData("(028) 3822-1234")]
    [InlineData("")]
    public void ValidPhones_AreAccepted(string value)
    {
        Assert.Null(SystemSettingRules.FindValueError(Key, value));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1234567")]
    [InlineData("12345678901234567")]
    [InlineData("0901 abc 567")]
    [InlineData("0901+234567")]
    public void InvalidPhones_AreRejectedWithVietnameseMessage(string value)
    {
        string? error = SystemSettingRules.FindValueError(Key, value);

        Assert.NotNull(error);
        Assert.Contains("Số điện thoại công ty không hợp lệ", error);
    }
}
