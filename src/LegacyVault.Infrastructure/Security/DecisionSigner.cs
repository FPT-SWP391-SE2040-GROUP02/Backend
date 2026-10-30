using System.Security.Cryptography;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// DEATH-14: ký số quyết định của Verifier bằng RSA-2048 + SHA-256 + PSS, dùng lại DataKeyProtector của tài sản.
/// </summary>
/// <remarks>
/// - D4: khóa riêng PKCS#8 mã hóa envelope giống BeneficiaryDataProtector: DEK mới → AES-256-GCM khóa riêng → bọc DEK bằng KEK.
///   DataKeyProtector.Wrap chỉ nhận 32 byte nên không bọc thẳng khóa riêng.
/// - D7: kiểm chữ ký bằng khóa công khai, rồi mở khóa riêng đã bọc để đối chiếu đúng cặp. Kẻ sửa DB không có KEK
///   nên không thay được cả khóa công khai lẫn khóa riêng đã bọc.
/// - Mảng byte chứa DEK và khóa riêng được xóa khỏi RAM ngay sau khi dùng.
/// </remarks>
public sealed class DecisionSigner : IDecisionSigner
{
    private const int KeySizeInBits = 2048;

    private readonly DataKeyProtector dataKeyProtector;

    public DecisionSigner(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public VerifierSigningKey CreateKey(Guid userId, DateTimeOffset now)
    {
        VerifierSigningKey key = new VerifierSigningKey();
        key.UserId = userId;
        key.Algorithm = DecisionSignatureRules.Algorithm;
        key.CreatedAt = now;

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] privateKey = Array.Empty<byte>();
        try
        {
            using (RSA rsa = RSA.Create(KeySizeInBits))
            {
                privateKey = rsa.ExportPkcs8PrivateKey();
                key.PublicKey = rsa.ExportSubjectPublicKeyInfo();
            }

            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            key.EncryptionKeyId = wrapped.KeyId;
            key.WrappedDataKey = wrapped.Value;
            key.PrivateKeyEncrypted = AesGcmCipher.Encrypt(dataKey, privateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(privateKey);
        }
        return key;
    }

    public byte[] Sign(VerifierSigningKey key, byte[] payload)
    {
        byte[] privateKey = OpenPrivateKey(key);
        try
        {
            using (RSA rsa = RSA.Create())
            {
                int bytesRead;
                rsa.ImportPkcs8PrivateKey(privateKey, out bytesRead);
                return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    public bool Verify(VerifierSigningKey key, byte[] payload, byte[] signature)
    {
        if (key.Algorithm != DecisionSignatureRules.Algorithm)
        {
            return false;
        }

        try
        {
            // (a) Chữ ký phải đúng với khóa công khai đã lưu.
            bool signatureMatches;
            using (RSA publicRsa = RSA.Create())
            {
                int bytesRead;
                publicRsa.ImportSubjectPublicKeyInfo(key.PublicKey, out bytesRead);
                signatureMatches = publicRsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            }
            if (!signatureMatches)
            {
                return false;
            }

            // (b) D7: khóa riêng đã bọc phải đúng là cặp của khóa công khai (chặn thay PublicKey + Signature trong DB).
            return PrivateKeyMatchesPublicKey(key);
        }
        catch (CryptographicException)
        {
            // Thiếu KEK, khóa đã bọc bị sửa hoặc khóa công khai hỏng: coi như chữ ký không hợp lệ.
            return false;
        }
    }

    private bool PrivateKeyMatchesPublicKey(VerifierSigningKey key)
    {
        byte[] privateKey = OpenPrivateKey(key);
        try
        {
            using (RSA rsa = RSA.Create())
            {
                int bytesRead;
                rsa.ImportPkcs8PrivateKey(privateKey, out bytesRead);
                byte[] derivedPublicKey = rsa.ExportSubjectPublicKeyInfo();
                if (derivedPublicKey.Length != key.PublicKey.Length)
                {
                    return false;
                }
                return CryptographicOperations.FixedTimeEquals(derivedPublicKey, key.PublicKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    /// <summary>Mở DEK bằng KEK rồi giải mã khóa riêng. Người gọi phải xóa mảng trả về sau khi dùng.</summary>
    private byte[] OpenPrivateKey(VerifierSigningKey key)
    {
        byte[] dataKey = dataKeyProtector.Unwrap(key.EncryptionKeyId, key.WrappedDataKey);
        try
        {
            return AesGcmCipher.Decrypt(dataKey, key.PrivateKeyEncrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }
}
