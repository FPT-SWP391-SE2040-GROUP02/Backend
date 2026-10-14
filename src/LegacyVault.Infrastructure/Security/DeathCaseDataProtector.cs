using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope phần khai báo nhạy cảm của phiên bản hồ sơ chứng tử (DATA-02, D6), theo mẫu BeneficiaryDataProtector.
/// </summary>
/// <remarks>
/// - Mỗi lần Protect sinh một DEK mới (32 byte); DB chỉ lưu DEK đã bọc bằng KEK.
/// - Họ tên Owner, CCCD Owner, số giấy, nơi cấp mã hóa chung một gói AES-256-GCM;
///   kết quả = nonce (12 byte) + ciphertext + tag (16 byte).
/// - AAD = "death-case-version:{Id}": chép gói sang phiên bản khác thì giải mã thất bại.
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class DeathCaseDataProtector : IDeathCaseDataProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly DataKeyProtector dataKeyProtector;

    public DeathCaseDataProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(DeathCaseVersion version, DeathCaseIdentityData data)
    {
        if (data.OwnerCitizenId != null && data.OwnerCitizenId.Length < 4)
        {
            throw new ArgumentException("Số CCCD chưa được chuẩn hóa.", nameof(data));
        }

        // Bản nháp chưa nhập trường nhạy cảm nào: không giữ gói mã hóa rỗng.
        if (data.OwnerFullName == null && data.OwnerCitizenId == null
            && data.CertificateNumber == null && data.IssuedBy == null)
        {
            version.EncryptionKeyId = null;
            version.WrappedDataKey = null;
            version.IdentityEncrypted = null;
            version.OwnerCitizenIdLast4 = null;
            return;
        }

        IdentityPayload payload = new IdentityPayload();
        payload.OwnerFullName = data.OwnerFullName;
        payload.OwnerCitizenId = data.OwnerCitizenId;
        payload.CertificateNumber = data.CertificateNumber;
        payload.IssuedBy = data.IssuedBy;

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plainText = Array.Empty<byte>();
        try
        {
            plainText = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            version.EncryptionKeyId = wrapped.KeyId;
            version.WrappedDataKey = wrapped.Value;
            version.IdentityEncrypted = Encrypt(dataKey, plainText, BuildAad(version.Id));
            if (data.OwnerCitizenId != null)
            {
                version.OwnerCitizenIdLast4 = data.OwnerCitizenId.Substring(data.OwnerCitizenId.Length - 4);
            }
            else
            {
                version.OwnerCitizenIdLast4 = null;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plainText);
        }
    }

    public DeathCaseIdentityData? Unprotect(DeathCaseVersion version)
    {
        if (version.IdentityEncrypted == null)
        {
            return null;
        }
        if (version.EncryptionKeyId == null || version.WrappedDataKey == null)
        {
            throw new CryptographicException("Phiên bản hồ sơ chứng tử thiếu khóa giải mã.");
        }

        byte[] dataKey = dataKeyProtector.Unwrap(version.EncryptionKeyId, version.WrappedDataKey);
        byte[] plainText = Array.Empty<byte>();
        try
        {
            plainText = Decrypt(dataKey, version.IdentityEncrypted, BuildAad(version.Id));
            IdentityPayload? payload = JsonSerializer.Deserialize<IdentityPayload>(plainText, JsonOptions);
            if (payload == null)
            {
                throw new CryptographicException("Dữ liệu hồ sơ chứng tử sau giải mã không hợp lệ.");
            }

            DeathCaseIdentityData data = new DeathCaseIdentityData();
            data.OwnerFullName = payload.OwnerFullName;
            data.OwnerCitizenId = payload.OwnerCitizenId;
            data.CertificateNumber = payload.CertificateNumber;
            data.IssuedBy = payload.IssuedBy;
            return data;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plainText);
        }
    }

    private static byte[] BuildAad(Guid versionId)
    {
        return Encoding.UTF8.GetBytes($"death-case-version:{versionId}");
    }

    /// <summary>Kết quả = nonce (12) + ciphertext (cùng độ dài bản rõ) + tag (16).</summary>
    private static byte[] Encrypt(byte[] dataKey, byte[] plainText, byte[] aad)
    {
        byte[] result = new byte[NonceSize + plainText.Length + TagSize];
        Span<byte> nonce = result.AsSpan(0, NonceSize);
        Span<byte> cipherText = result.AsSpan(NonceSize, plainText.Length);
        Span<byte> tag = result.AsSpan(NonceSize + plainText.Length, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using (AesGcm aes = new AesGcm(dataKey, TagSize))
        {
            aes.Encrypt(nonce, plainText, cipherText, tag, aad);
        }
        return result;
    }

    /// <summary>Sai tag (dữ liệu/AAD bị sửa) → AuthenticationTagMismatchException (là CryptographicException).</summary>
    private static byte[] Decrypt(byte[] dataKey, byte[] encrypted, byte[] aad)
    {
        if (encrypted.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Dữ liệu hồ sơ chứng tử đã mã hóa có độ dài không hợp lệ.");
        }

        int cipherLength = encrypted.Length - NonceSize - TagSize;
        byte[] plainText = new byte[cipherLength];
        using (AesGcm aes = new AesGcm(dataKey, TagSize))
        {
            aes.Decrypt(
                encrypted.AsSpan(0, NonceSize),
                encrypted.AsSpan(NonceSize, cipherLength),
                encrypted.AsSpan(NonceSize + cipherLength, TagSize),
                plainText,
                aad);
        }
        return plainText;
    }

    /// <summary>Gói dữ liệu trong IdentityEncrypted.</summary>
    private sealed class IdentityPayload
    {
        public string? OwnerFullName { get; set; }

        public string? OwnerCitizenId { get; set; }

        public string? CertificateNumber { get; set; }

        public string? IssuedBy { get; set; }
    }
}
