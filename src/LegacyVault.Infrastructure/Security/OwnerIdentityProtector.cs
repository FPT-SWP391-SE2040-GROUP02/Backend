using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope danh tính Owner (DATA-02, EKYC-27), dùng lại DataKeyProtector của tài sản.
/// </summary>
/// <remarks>
/// - Mỗi lần Protect sinh một DEK mới (32 byte); DB chỉ lưu DEK đã bọc bằng KEK.
/// - Cột mã hóa = nonce (12) + ciphertext + tag (16), AES-256-GCM.
/// - AAD = "owner-identity:{Id}" cho danh tính đã gắn, "owner-identity-pending:{Id}" cho bản tạm trên lượt eKYC: chép
///   cột sang dòng khác hoặc đổi giữa hai nơi thì giải mã thất bại.
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class OwnerIdentityProtector : IOwnerIdentityProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly DataKeyProtector dataKeyProtector;

    public OwnerIdentityProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(OwnerIdentity identity, OwnerIdentityData data)
    {
        if (data.CitizenId.Length < 4)
        {
            throw new ArgumentException("Số CCCD chưa được chuẩn hóa.", nameof(data));
        }

        WrappedDataKey wrapped;
        byte[] encrypted = EncryptPayload(data, BuildAad("owner-identity", identity.Id), out wrapped);
        identity.EncryptionKeyId = wrapped.KeyId;
        identity.WrappedDataKey = wrapped.Value;
        identity.IdentityEncrypted = encrypted;
        identity.CitizenIdLast4 = data.CitizenId.Substring(data.CitizenId.Length - 4);
    }

    public OwnerIdentityData Unprotect(OwnerIdentity identity)
    {
        return DecryptPayload(
            identity.EncryptionKeyId, identity.WrappedDataKey, identity.IdentityEncrypted,
            BuildAad("owner-identity", identity.Id));
    }

    public void ProtectPending(EkycVerification verification, OwnerIdentityData data)
    {
        if (data.CitizenId.Length < 4)
        {
            throw new ArgumentException("Số CCCD chưa được chuẩn hóa.", nameof(data));
        }

        WrappedDataKey wrapped;
        byte[] encrypted = EncryptPayload(data, BuildAad("owner-identity-pending", verification.Id), out wrapped);
        verification.EnrollmentIdentityKeyId = wrapped.KeyId;
        verification.EnrollmentWrappedDataKey = wrapped.Value;
        verification.EnrollmentIdentityEncrypted = encrypted;
    }

    public OwnerIdentityData? UnprotectPending(EkycVerification verification)
    {
        if (verification.EnrollmentIdentityKeyId == null
            || verification.EnrollmentWrappedDataKey == null
            || verification.EnrollmentIdentityEncrypted == null)
        {
            return null;
        }
        return DecryptPayload(
            verification.EnrollmentIdentityKeyId, verification.EnrollmentWrappedDataKey,
            verification.EnrollmentIdentityEncrypted, BuildAad("owner-identity-pending", verification.Id));
    }

    private byte[] EncryptPayload(OwnerIdentityData data, byte[] aad, out WrappedDataKey wrapped)
    {
        IdentityPayload payload = new IdentityPayload();
        payload.CitizenId = data.CitizenId;
        payload.FullName = data.FullName;
        payload.DateOfBirth = data.DateOfBirth;

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            wrapped = dataKeyProtector.Wrap(dataKey);
            return Encrypt(dataKey, plain, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private OwnerIdentityData DecryptPayload(string keyId, byte[] wrappedKey, byte[] encrypted, byte[] aad)
    {
        byte[] dataKey = dataKeyProtector.Unwrap(keyId, wrappedKey);
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = Decrypt(dataKey, encrypted, aad);
            IdentityPayload? payload = JsonSerializer.Deserialize<IdentityPayload>(plain, JsonOptions);
            if (payload == null)
            {
                throw new CryptographicException("Danh tính Owner sau giải mã không hợp lệ.");
            }

            OwnerIdentityData data = new OwnerIdentityData();
            data.CitizenId = payload.CitizenId;
            data.FullName = payload.FullName;
            data.DateOfBirth = payload.DateOfBirth;
            return data;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] BuildAad(string kind, Guid id)
    {
        return Encoding.UTF8.GetBytes($"{kind}:{id}");
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
            throw new CryptographicException("Danh tính Owner đã mã hóa có độ dài không hợp lệ.");
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

    private sealed class IdentityPayload
    {
        public string CitizenId { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly DateOfBirth { get; set; }
    }
}
