using System.Security.Cryptography;
using System.Text;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Security;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>Khóa API/mật khẩu của cấu hình dịch vụ chỉ lưu dạng mã hóa envelope (KEK ở biến môi trường).</summary>
public sealed class SystemConfigSecretProtectorTests
{
    private const string Secret = "sk-test-123456";

    private static SystemConfigSecretProtector NewProtector()
    {
        AssetEncryptionSettings settings = new AssetEncryptionSettings();
        settings.ActiveKeyId = "k1";
        settings.Keys["k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(DataKeyProtector.KeySize));
        return new SystemConfigSecretProtector(new DataKeyProtector(Options.Create(settings)));
    }

    private static Dictionary<string, string> OneSecret()
    {
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        secrets["apiKey"] = Secret;
        return secrets;
    }

    [Fact]
    public void RoundTrip_ReturnsSameSecrets()
    {
        SystemConfigSecretProtector protector = NewProtector();
        SystemConfig row = new SystemConfig();

        protector.Protect(row, OneSecret());
        Dictionary<string, string> opened = protector.Unprotect(row);

        Assert.Equal("k1", row.EncryptionKeyId);
        Assert.Equal(Secret, opened["apiKey"]);
        Assert.Single(opened);
    }

    [Fact]
    public void Ciphertext_DoesNotContainPlaintext()
    {
        SystemConfigSecretProtector protector = NewProtector();
        SystemConfig row = new SystemConfig();

        protector.Protect(row, OneSecret());

        byte[] plain = Encoding.UTF8.GetBytes(Secret);
        Assert.False(row.SecretsEncrypted.AsSpan().IndexOf(plain) >= 0);
    }

    [Fact]
    public void TamperedCiphertext_ThrowsCryptographicException()
    {
        SystemConfigSecretProtector protector = NewProtector();
        SystemConfig row = new SystemConfig();
        protector.Protect(row, OneSecret());

        row.SecretsEncrypted[row.SecretsEncrypted.Length - 1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(row));
    }

    [Fact]
    public void EmptySecrets_StoreNothingAndNeedNoKek()
    {
        SystemConfigSecretProtector protector = NewProtector();
        SystemConfig row = new SystemConfig();

        protector.Protect(row, new Dictionary<string, string>());

        Assert.Equal(string.Empty, row.EncryptionKeyId);
        Assert.Empty(row.WrappedDataKey);
        Assert.Empty(row.SecretsEncrypted);
        Assert.Empty(protector.Unprotect(row));
    }
}
