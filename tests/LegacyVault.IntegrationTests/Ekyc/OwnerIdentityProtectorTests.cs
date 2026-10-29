using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Mã hóa danh tính Owner (DATA-02, EKYC-27): đúng chiều, không lộ bản rõ, phát hiện sửa/đổi chỗ.</summary>
public sealed class OwnerIdentityProtectorTests
{
    private readonly OwnerIdentityProtector protector = FaceTemplateTestSupport.CreateIdentityProtector();

    private static OwnerIdentityData Data()
    {
        OwnerIdentityData data = new OwnerIdentityData();
        data.CitizenId = "079200012345";
        data.FullName = "NGUYỄN VĂN AN";
        data.DateOfBirth = new DateOnly(2000, 2, 1);
        return data;
    }

    [Fact]
    public void Identity_RoundTrip_KeepsAllFieldsAndLast4()
    {
        OwnerIdentity identity = new OwnerIdentity();
        protector.Protect(identity, Data());

        OwnerIdentityData result = protector.Unprotect(identity);

        Assert.Equal("079200012345", result.CitizenId);
        Assert.Equal("NGUYỄN VĂN AN", result.FullName);
        Assert.Equal(new DateOnly(2000, 2, 1), result.DateOfBirth);
        Assert.Equal("2345", identity.CitizenIdLast4);
        Assert.Equal(60, identity.WrappedDataKey.Length);
        Assert.Equal("test-key", identity.EncryptionKeyId);
    }

    [Fact]
    public void Identity_Ciphertext_DoesNotContainCitizenIdOrName()
    {
        OwnerIdentity identity = new OwnerIdentity();
        protector.Protect(identity, Data());

        Assert.False(identity.IdentityEncrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes("079200012345")) >= 0);
        Assert.False(identity.IdentityEncrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes("NGUY")) >= 0);
    }

    [Fact]
    public void Identity_Tampered_Throws()
    {
        OwnerIdentity tampered = new OwnerIdentity();
        protector.Protect(tampered, Data());
        tampered.IdentityEncrypted[15] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Pending_RoundTrip_AndNullWhenAbsent()
    {
        EkycVerification verification = new EkycVerification();
        Assert.Null(protector.UnprotectPending(verification));

        protector.ProtectPending(verification, Data());
        OwnerIdentityData result = protector.UnprotectPending(verification)!;

        Assert.Equal("079200012345", result.CitizenId);
        Assert.Equal(60, verification.EnrollmentWrappedDataKey!.Length);
    }

    [Fact]
    public void ShortCitizenId_IsRejected()
    {
        OwnerIdentityData data = Data();
        data.CitizenId = "12";

        Assert.Throws<ArgumentException>(() => protector.Protect(new OwnerIdentity(), data));
    }
}
