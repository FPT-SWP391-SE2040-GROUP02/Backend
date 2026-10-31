using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Security;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>DEATH-14: tạo khóa RSA, ký và kiểm chữ ký quyết định của Verifier (D4, D7). Không cần SQL.</summary>
public sealed class DecisionSignerTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("LV-DEATH-DECISION|v1|thu");

    [Fact]
    public void Death14_SignThenVerifyIsValid()
    {
        // Chuẩn bị
        DecisionSigner signer = CreateSigner(CreateDataKeyProtector(NewKek()));
        VerifierSigningKey key = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Hành động
        byte[] signature = signer.Sign(key, Payload);
        bool isValid = signer.Verify(key, Payload, signature);

        // Kiểm tra
        Assert.True(isValid);
        Assert.Equal(256, signature.Length);
        Assert.Equal(DecisionSignatureRules.Algorithm, key.Algorithm);
        Assert.Equal("test-key", key.EncryptionKeyId);
        Assert.Equal(60, key.WrappedDataKey.Length);
    }

    [Fact]
    public void Death14_ChangedPayloadIsInvalid()
    {
        // Chuẩn bị
        DecisionSigner signer = CreateSigner(CreateDataKeyProtector(NewKek()));
        VerifierSigningKey key = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] signature = signer.Sign(key, Payload);
        byte[] changed = (byte[])Payload.Clone();
        changed[changed.Length - 1] = (byte)(changed[changed.Length - 1] ^ 0x01);

        // Hành động
        bool isValid = signer.Verify(key, changed, signature);

        // Kiểm tra
        Assert.False(isValid);
    }

    [Fact]
    public void Death14_ChangedSignatureIsInvalid()
    {
        // Chuẩn bị
        DecisionSigner signer = CreateSigner(CreateDataKeyProtector(NewKek()));
        VerifierSigningKey key = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] signature = signer.Sign(key, Payload);
        signature[10] = (byte)(signature[10] ^ 0x01);

        // Hành động
        bool isValid = signer.Verify(key, Payload, signature);
        bool emptyIsValid = signer.Verify(key, Payload, Array.Empty<byte>());

        // Kiểm tra
        Assert.False(isValid);
        Assert.False(emptyIsValid);
    }

    [Fact]
    public void Death14_SwappedPublicKeyIsInvalid()
    {
        // Chuẩn bị: kẻ sửa DB chép khóa công khai của khóa thứ hai sang khóa thứ nhất rồi ký lại bằng khóa thứ hai
        DecisionSigner signer = CreateSigner(CreateDataKeyProtector(NewKek()));
        VerifierSigningKey victim = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        VerifierSigningKey attacker = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] forgedSignature = signer.Sign(attacker, Payload);
        victim.PublicKey = attacker.PublicKey;

        // Hành động
        bool isValid = signer.Verify(victim, Payload, forgedSignature);

        // Kiểm tra: chữ ký khớp khóa công khai nhưng khóa riêng đã bọc không phải cặp → không hợp lệ
        Assert.False(isValid);
        Assert.True(signer.Verify(attacker, Payload, forgedSignature));
    }

    [Fact]
    public void Death14_PrivateKeyIsNotStoredInPlain()
    {
        // Chuẩn bị
        DataKeyProtector dataKeyProtector = CreateDataKeyProtector(NewKek());
        DecisionSigner signer = CreateSigner(dataKeyProtector);
        VerifierSigningKey key = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Hành động: mở khóa riêng bằng KEK như phía server
        byte[] dataKey = dataKeyProtector.Unwrap(key.EncryptionKeyId, key.WrappedDataKey);
        byte[] privateKey = AesGcmCipher.Decrypt(dataKey, key.PrivateKeyEncrypted);

        // Kiểm tra: bản mã không chứa PKCS#8 gốc (lấy một đoạn 64 byte giữa khóa), khóa riêng mở ra đúng cặp
        try
        {
            byte[] middle = privateKey.AsSpan(privateKey.Length / 2, 64).ToArray();
            Assert.False(Contains(key.PrivateKeyEncrypted, middle));
            Assert.False(Contains(key.PublicKey, middle));
            using (RSA rsa = RSA.Create())
            {
                int bytesRead;
                rsa.ImportPkcs8PrivateKey(privateKey, out bytesRead);
                Assert.Equal(key.PublicKey, rsa.ExportSubjectPublicKeyInfo());
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    [Fact]
    public void Death14_EachKeyUsesNewDataKey()
    {
        // Chuẩn bị
        DecisionSigner signer = CreateSigner(CreateDataKeyProtector(NewKek()));

        // Hành động
        VerifierSigningKey first = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        VerifierSigningKey second = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Kiểm tra
        Assert.NotEqual(first.WrappedDataKey, second.WrappedDataKey);
        Assert.NotEqual(first.PrivateKeyEncrypted, second.PrivateKeyEncrypted);
        Assert.NotEqual(first.PublicKey, second.PublicKey);
    }

    [Fact]
    public void Death14_WrongKekCannotVerify()
    {
        // Chuẩn bị: ký bằng server có KEK A; server khác cùng id KEK nhưng khác giá trị
        DecisionSigner signer = CreateSigner(CreateDataKeyProtector(NewKek()));
        DecisionSigner otherServer = CreateSigner(CreateDataKeyProtector(NewKek()));
        VerifierSigningKey key = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] signature = signer.Sign(key, Payload);
        VerifierSigningKey unknownKekKey = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] unknownKekSignature = signer.Sign(unknownKekKey, Payload);
        unknownKekKey.EncryptionKeyId = "missing-key";

        // Hành động
        Exception? error = Record.Exception(() => otherServer.Verify(key, Payload, signature));
        bool isValid = otherServer.Verify(key, Payload, signature);
        bool missingKekIsValid = signer.Verify(unknownKekKey, Payload, unknownKekSignature);

        // Kiểm tra: trả false, không ném
        Assert.Null(error);
        Assert.False(isValid);
        Assert.False(missingKekIsValid);
    }

    private static string NewKek()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static DataKeyProtector CreateDataKeyProtector(string kek)
    {
        AssetEncryptionSettings settings = new AssetEncryptionSettings();
        settings.ActiveKeyId = "test-key";
        settings.Keys["test-key"] = kek;
        return new DataKeyProtector(Options.Create(settings));
    }

    private static DecisionSigner CreateSigner(DataKeyProtector dataKeyProtector)
    {
        return new DecisionSigner(dataKeyProtector);
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
