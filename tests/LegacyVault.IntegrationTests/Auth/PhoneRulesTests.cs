using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Kiểm tra chuẩn hóa số di động Việt Nam khi đăng ký và khi nhập số sau đăng nhập Google.</summary>
public sealed class PhoneRulesTests
{
    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("091 234 5678", "0912345678")]
    [InlineData("091.234.5678", "0912345678")]
    [InlineData("+84912345678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("0312345678", "0312345678")]
    [InlineData("0812345678", "0812345678")]
    public void NormalizeVietnamPhone_ValidMobile_ReturnsTenDigits(string input, string expected)
    {
        Assert.Equal(expected, PhoneRules.NormalizeVietnamPhone(input, "phone"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("0212345678")]
    [InlineData("0612345678")]
    [InlineData("1912345678")]
    [InlineData("+1 415 555 0100")]
    [InlineData("09123abc78")]
    public void NormalizeVietnamPhone_Invalid_ThrowsValidation(string? input)
    {
        AppException error = Assert.Throws<AppException>(() => PhoneRules.NormalizeVietnamPhone(input, "phone"));
        Assert.Equal(AppErrorType.Validation, error.Type);
    }
}
