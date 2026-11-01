using System.Security.Cryptography;
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
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class OwnerIdentityProtector : IOwnerIdentityProtector
{

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
        byte[] encrypted = EncryptPayload(data, out wrapped);
        identity.EncryptionKeyId = wrapped.KeyId;
        identity.WrappedDataKey = wrapped.Value;
        identity.IdentityEncrypted = encrypted;
        identity.CitizenIdLast4 = data.CitizenId.Substring(data.CitizenId.Length - 4);
    }

    public OwnerIdentityData Unprotect(OwnerIdentity identity)
    {
        return DecryptPayload(
            identity.EncryptionKeyId, identity.WrappedDataKey, identity.IdentityEncrypted);
    }

    public void ProtectPending(EkycVerification verification, OwnerIdentityData data)
    {
        if (data.CitizenId.Length < 4)
        {
            throw new ArgumentException("Số CCCD chưa được chuẩn hóa.", nameof(data));
        }

        WrappedDataKey wrapped;
        byte[] encrypted = EncryptPayload(data, out wrapped);
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
            verification.EnrollmentIdentityEncrypted);
    }

    private byte[] EncryptPayload(OwnerIdentityData data, out WrappedDataKey wrapped)
    {
        IdentityPayload payload = new IdentityPayload();
        payload.CitizenId = data.CitizenId;
        payload.FullName = data.FullName;
        payload.DateOfBirth = data.DateOfBirth;
        payload.ResidenceAddress = data.ResidenceAddress;

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            wrapped = dataKeyProtector.Wrap(dataKey);
            return AesGcmCipher.Encrypt(dataKey, plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private OwnerIdentityData DecryptPayload(string keyId, byte[] wrappedKey, byte[] encrypted)
    {
        byte[] dataKey = dataKeyProtector.Unwrap(keyId, wrappedKey);
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = AesGcmCipher.Decrypt(dataKey, encrypted);
            IdentityPayload? payload = JsonSerializer.Deserialize<IdentityPayload>(plain, JsonOptions);
            if (payload == null)
            {
                throw new CryptographicException("Danh tính Owner sau giải mã không hợp lệ.");
            }

            OwnerIdentityData data = new OwnerIdentityData();
            data.CitizenId = payload.CitizenId;
            data.FullName = payload.FullName;
            data.DateOfBirth = payload.DateOfBirth;
            data.ResidenceAddress = payload.ResidenceAddress;
            return data;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }


    private sealed class IdentityPayload
    {
        public string CitizenId { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly DateOfBirth { get; set; }

        // Bản mã hóa cũ không có khóa này nên đọc ra chuỗi rỗng, không cần migration.
        public string ResidenceAddress { get; set; } = string.Empty;
    }
}
