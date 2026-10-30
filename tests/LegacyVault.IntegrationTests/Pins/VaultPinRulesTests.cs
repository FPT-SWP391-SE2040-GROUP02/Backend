using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Pins;

/// <summary>Luật chọn PIN và các mốc khóa: hàm thuần, không cần DB.</summary>
public sealed class VaultPinRulesTests
{
    [Theory]
    [InlineData("000000")]
    [InlineData("111111")]
    [InlineData("999999")]
    [InlineData("123456")]
    [InlineData("234567")]
    [InlineData("012345")]
    [InlineData("654321")]
    [InlineData("987654")]
    public void IsTooWeak_RejectsRepeatedDigitsAndSequences(string pin)
    {
        Assert.True(VaultPinRules.IsTooWeak(pin));
    }

    [Theory]
    [InlineData("482913")]
    [InlineData("135790")]
    [InlineData("123450")]
    [InlineData("112233")]
    public void IsTooWeak_AcceptsOrdinaryPins(string pin)
    {
        Assert.False(VaultPinRules.IsTooWeak(pin));
    }

    [Theory]
    [InlineData("482913", true)]
    [InlineData("000000", true)]
    [InlineData("48291", false)]
    [InlineData("4829130", false)]
    [InlineData("48291a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWellFormed_RequiresExactlySixDigits(string? pin, bool expected)
    {
        Assert.Equal(expected, VaultPinRules.IsWellFormed(pin));
    }

    [Fact]
    public void LockDuration_EscalatesThenStaysAtTheLastLevel()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), VaultPinRules.LockDuration(1));
        Assert.Equal(TimeSpan.FromHours(1), VaultPinRules.LockDuration(2));
        Assert.Equal(TimeSpan.FromHours(24), VaultPinRules.LockDuration(3));
        Assert.Equal(TimeSpan.FromHours(24), VaultPinRules.LockDuration(9));
        Assert.Equal(3, VaultPinRules.MaxLockLevel);
    }

    [Fact]
    public void EnsureAcceptable_RejectsMismatchedConfirmationAsValidationError()
    {
        AppException error = Assert.Throws<AppException>(
            () => VaultPinRules.EnsureAcceptable("482913", "482914", "pin", "confirmPin"));

        Assert.Equal(AppErrorType.Validation, error.Type);
        Assert.Equal(ErrorCode.ValidationError, error.Code);
    }

    [Fact]
    public void EnsureAcceptable_RejectsWeakPinWithDedicatedCode()
    {
        AppException error = Assert.Throws<AppException>(
            () => VaultPinRules.EnsureAcceptable("123456", "123456", "pin", "confirmPin"));

        Assert.Equal(AppErrorType.Validation, error.Type);
        Assert.Equal(ErrorCode.VaultPinTooWeak, error.Code);
    }

    [Fact]
    public void EnsureAcceptable_AcceptsGoodPin()
    {
        VaultPinRules.EnsureAcceptable("482913", "482913", "pin", "confirmPin");
    }
}
