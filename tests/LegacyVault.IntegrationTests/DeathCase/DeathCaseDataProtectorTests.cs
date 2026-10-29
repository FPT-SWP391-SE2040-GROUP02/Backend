using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Security;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Mã hóa phần khai báo nhạy cảm của phiên bản hồ sơ chứng tử (DATA-02, D6). Không cần SQL.</summary>
public sealed class DeathCaseDataProtectorTests
{
    private const string CitizenId = "012345678901";

    [Fact]
    public void Data02_ProtectAndUnprotectRoundTrip()
    {
        // Chuẩn bị
        DeathCaseDataProtector protector = CreateProtector();
        DeathCaseVersion version = new DeathCaseVersion();
        DeathCaseVersion empty = new DeathCaseVersion();

        // Hành động
        protector.Protect(version, CreateData());
        DeathCaseIdentityData? data = protector.Unprotect(version);
        protector.Protect(empty, new DeathCaseIdentityData());
        DeathCaseIdentityData? emptyData = protector.Unprotect(empty);

        // Kiểm tra
        Assert.NotNull(data);
        Assert.Equal("Nguyễn Văn An", data.OwnerFullName);
        Assert.Equal(CitizenId, data.OwnerCitizenId);
        Assert.Equal("123/2026/TT", data.CertificateNumber);
        Assert.Equal("UBND phường Bến Nghé", data.IssuedBy);
        Assert.Equal("8901", version.OwnerCitizenIdLast4);
        Assert.Equal("test-key", version.EncryptionKeyId);
        // Bản nháp chưa nhập gì: không có gói mã hóa, Unprotect trả null.
        Assert.Null(empty.IdentityEncrypted);
        Assert.Null(empty.WrappedDataKey);
        Assert.Null(empty.OwnerCitizenIdLast4);
        Assert.Null(emptyData);
    }

    [Fact]
    public void Data02_EncryptedBytesDoNotContainCitizenId()
    {
        // Chuẩn bị
        DeathCaseDataProtector protector = CreateProtector();
        DeathCaseVersion version = new DeathCaseVersion();

        // Hành động
        protector.Protect(version, CreateData());

        // Kiểm tra
        Assert.NotNull(version.IdentityEncrypted);
        Assert.NotNull(version.WrappedDataKey);
        Assert.False(Contains(version.IdentityEncrypted, Encoding.UTF8.GetBytes(CitizenId)));
        Assert.False(Contains(version.IdentityEncrypted, Encoding.UTF8.GetBytes("Nguyễn Văn An")));
        Assert.False(Contains(version.IdentityEncrypted, Encoding.UTF8.GetBytes("123/2026/TT")));
    }

    [Fact]
    public void Sec02_TamperedVersionFailsToDecrypt()
    {
        // Chuẩn bị
        DeathCaseDataProtector protector = CreateProtector();
        DeathCaseVersion version = new DeathCaseVersion();
        protector.Protect(version, CreateData());
        DeathCaseVersion flipped = new DeathCaseVersion();
        flipped.Id = version.Id;
        flipped.EncryptionKeyId = version.EncryptionKeyId;
        flipped.WrappedDataKey = version.WrappedDataKey;
        byte[] tampered = (byte[])version.IdentityEncrypted!.Clone();
        tampered[20] = (byte)(tampered[20] ^ 0x01);
        flipped.IdentityEncrypted = tampered;

        // Hành động
        Exception? flippedError = Record.Exception(() => protector.Unprotect(flipped));

        // Kiểm tra: sửa một bit thì không giải mã được
        Assert.IsAssignableFrom<CryptographicException>(flippedError);
    }

    [Fact]
    public void Sec02_EachSaveUsesNewDataKey()
    {
        // Chuẩn bị
        DeathCaseDataProtector protector = CreateProtector();
        DeathCaseVersion version = new DeathCaseVersion();

        // Hành động
        protector.Protect(version, CreateData());
        byte[] firstKey = version.WrappedDataKey!;
        byte[] firstData = version.IdentityEncrypted!;
        protector.Protect(version, CreateData());

        // Kiểm tra
        Assert.NotEqual(firstKey, version.WrappedDataKey);
        Assert.NotEqual(firstData, version.IdentityEncrypted);
        DeathCaseIdentityData? data = protector.Unprotect(version);
        Assert.NotNull(data);
        Assert.Equal(CitizenId, data.OwnerCitizenId);
    }

    private static DeathCaseIdentityData CreateData()
    {
        DeathCaseIdentityData data = new DeathCaseIdentityData();
        data.OwnerFullName = "Nguyễn Văn An";
        data.OwnerCitizenId = CitizenId;
        data.CertificateNumber = "123/2026/TT";
        data.IssuedBy = "UBND phường Bến Nghé";
        return data;
    }

    private static DeathCaseDataProtector CreateProtector()
    {
        AssetEncryptionSettings settings = new AssetEncryptionSettings();
        settings.ActiveKeyId = "test-key";
        settings.Keys["test-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        DataKeyProtector dataKeyProtector = new DataKeyProtector(Options.Create(settings));
        return new DeathCaseDataProtector(dataKeyProtector);
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
