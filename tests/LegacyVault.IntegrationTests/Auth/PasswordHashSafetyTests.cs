using LegacyVault.Infrastructure.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Regression hash chuyển từ test: từ chối tham số/tài nguyên do hash không tin cậy yêu cầu.</summary>
public sealed class PasswordHashSafetyTests
{
    [Fact]
    public void PreservesExistingHashFormatAndPasswordBytes()
    {
        var hasher = new Argon2PasswordHasher();
        string hash = hasher.Hash(" Abcdefg! ");
        Assert.StartsWith("argon2id$m=19456,t=2,p=1$", hash);
        Assert.True(hasher.Verify(" Abcdefg! ", hash));
        Assert.False(hasher.Verify("Abcdefg!", hash));
        Assert.False(hasher.Verify(new string('x', 129), hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("argon2id$m=2147483647,t=2,p=1$AAAA$AAAA")]
    [InlineData("argon2id$m=19456,t=2,p=1$invalid$invalid")]
    [InlineData("argon2id$m=1$AAAA$AAAA")]
    public void MalformedHashReturnsFalseWithoutExecutingUntrustedParameters(string hash)
    {
        Assert.False(new Argon2PasswordHasher().Verify("fixture", hash));
    }
}
