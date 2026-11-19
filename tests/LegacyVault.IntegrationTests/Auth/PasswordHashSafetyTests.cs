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

/// <summary>PBKDF2 mới + nâng cấp dần từ hash SHA-256 cũ (dùng ít vòng cho nhanh; định dạng và logic giống bản thật).</summary>
public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new Pbkdf2PasswordHasher(1000);

    [Fact]
    public void NewHash_HasFormatWithIterationsAndVerifies()
    {
        string hash = _hasher.Hash("Matkhau123!");
        Assert.StartsWith("pbkdf2$1000$", hash);
        Assert.True(_hasher.Verify("Matkhau123!", hash));
        Assert.False(_hasher.Verify("sai", hash));
        Assert.False(_hasher.NeedsRehash(hash));
    }

    [Fact]
    public void LegacySha256Hash_StillVerifies_AndNeedsRehash()
    {
        string legacy = new Sha256PasswordHasher().Hash("Matkhau123!");
        Assert.True(_hasher.Verify("Matkhau123!", legacy));
        Assert.False(_hasher.Verify("sai", legacy));
        Assert.True(_hasher.NeedsRehash(legacy));
    }

    [Fact]
    public void FewerIterationsThanConfigured_NeedsRehash()
    {
        string old = new Pbkdf2PasswordHasher(500).Hash("x");
        Assert.True(_hasher.Verify("x", old));
        Assert.True(_hasher.NeedsRehash(old));
    }

    [Theory]
    [InlineData("pbkdf2$1000$AAAA")]
    [InlineData("pbkdf2$abc$AAAA$AAAA")]
    [InlineData("pbkdf2$0$AAAA$AAAA")]
    [InlineData("pbkdf2$999999999$AAAA$AAAA")]
    [InlineData("pbkdf2$1000$!!!$???")]
    [InlineData("pbkdf2$1000$$")]
    public void MalformedNewFormat_ReturnsFalse_NoException(string hash)
    {
        Assert.False(_hasher.Verify("fixture", hash));
    }
}
