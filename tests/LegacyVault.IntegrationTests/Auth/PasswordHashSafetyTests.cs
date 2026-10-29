using LegacyVault.Infrastructure.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Hash SHA-256 + salt: đúng mật khẩu thì khớp, sai mật khẩu hoặc hash sai định dạng thì không.</summary>
public sealed class PasswordHashSafetyTests
{
    [Fact]
    public void VerifiesOnlyTheOriginalPassword()
    {
        var hasher = new Sha256PasswordHasher();
        string hash = hasher.Hash(" Abcdefg! ");
        Assert.True(hasher.Verify(" Abcdefg! ", hash));
        Assert.False(hasher.Verify("Abcdefg!", hash));
    }

    [Fact]
    public void SamePasswordProducesDifferentHashes()
    {
        var hasher = new Sha256PasswordHasher();
        Assert.NotEqual(hasher.Hash("Abcdefg!"), hasher.Hash("Abcdefg!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong-co-dau-phan-cach")]
    [InlineData("argon2id$m=19456,t=2,p=1$AAAA$AAAA")]
    public void MalformedHashReturnsFalse(string hash)
    {
        Assert.False(new Sha256PasswordHasher().Verify("fixture", hash));
    }
}
