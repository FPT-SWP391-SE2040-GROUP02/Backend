using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Chuẩn hóa số điện thoại khi đăng ký và khi nhập số sau đăng nhập Google: chỉ bỏ ký tự phân cách, không kiểm định dạng.</summary>
public sealed class PhoneRulesTests
{
    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("091 234 5678", "0912345678")]
    [InlineData("091.234.5678", "0912345678")]
    [InlineData("+84912345678", "+84912345678")]
    [InlineData("(028) 3822-1234", "02838221234")]
    [InlineData("12345", "12345")]
    [InlineData("1912345678", "1912345678")]
    public void NormalizeContactPhone_AnyNumberIsAccepted_OnlySeparatorsAreStripped(string input, string expected)
    {
        Assert.Equal(expected, PhoneRules.NormalizeContactPhone(input, "phone"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizeContactPhone_Empty_ThrowsRequired(string? input)
    {
        AppException error = Assert.Throws<AppException>(() => PhoneRules.NormalizeContactPhone(input, "phone"));
        Assert.Equal(AppErrorType.Validation, error.Type);
    }

    [Fact]
    public void NormalizeContactPhone_TooLong_ThrowsValidation()
    {
        AppException error = Assert.Throws<AppException>(
            () => PhoneRules.NormalizeContactPhone(new string('1', PhoneRules.MaxLength + 1), "phone"));
        Assert.Equal(AppErrorType.Validation, error.Type);
    }

    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("091 234 5678", "0912345678")]
    [InlineData("(091) 234-5678", "0912345678")]
    [InlineData("+84912345678", "+84912345678")]
    [InlineData("0398765432", "0398765432")]
    [InlineData("0587654321", "0587654321")]
    [InlineData("0701234567", "0701234567")]
    [InlineData("0851234567", "0851234567")]
    public void NormalizeVietnamesePhone_MobileNumber_IsAccepted(string input, string expected)
    {
        Assert.Equal(expected, PhoneRules.NormalizeVietnamesePhone(input, "phone"));
    }

    [Theory]
    [InlineData("3232")]
    [InlineData("12345")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("0212345678")]
    [InlineData("0112345678")]
    [InlineData("1912345678")]
    [InlineData("84912345678")]
    [InlineData("+8401234567")]
    [InlineData("abc0912345678")]
    [InlineData("0912345678x")]
    public void NormalizeVietnamesePhone_WrongFormat_ThrowsValidation(string input)
    {
        AppException error = Assert.Throws<AppException>(() => PhoneRules.NormalizeVietnamesePhone(input, "phone"));
        Assert.Equal(AppErrorType.Validation, error.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizeVietnamesePhone_Empty_ThrowsRequired(string? input)
    {
        AppException error = Assert.Throws<AppException>(() => PhoneRules.NormalizeVietnamesePhone(input, "phone"));
        Assert.Equal(AppErrorType.Validation, error.Type);
    }
}
