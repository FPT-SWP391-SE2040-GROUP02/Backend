using System.Security.Cryptography;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope dữ liệu cá nhân của người nhận (DATA-02, §11.6), dùng lại DataKeyProtector của tài sản.
/// </summary>
/// <remarks>
/// - Mỗi lần Protect sinh một DEK mới (32 byte); DB chỉ lưu DEK đã bọc bằng KEK.
/// - Mỗi cột mã hóa bằng AES-256-GCM với DEK; kết quả = nonce (12 byte) + ciphertext + tag (16 byte).
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class BeneficiaryDataProtector : IBeneficiaryDataProtector
{
    private const string IdentityPart = "identity";
    private const string ContactPart = "contact";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly DataKeyProtector dataKeyProtector;

    public BeneficiaryDataProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(Beneficiary beneficiary, BeneficiaryPersonalData data)
    {
        if (data.CitizenId.Length < 4)
        {
            throw new ArgumentException("Số CCCD chưa được chuẩn hóa.", nameof(data));
        }

        IdentityPayload identity = new IdentityPayload();
        identity.CitizenId = data.CitizenId;
        identity.FullName = data.FullName;
        identity.DateOfBirth = data.DateOfBirth;

        ContactPayload contact = new ContactPayload();
        contact.Email = data.Email;
        contact.Phone = data.Phone;

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] identityPlain = Array.Empty<byte>();
        byte[] contactPlain = Array.Empty<byte>();
        try
        {
            identityPlain = JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);
            contactPlain = JsonSerializer.SerializeToUtf8Bytes(contact, JsonOptions);

            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            beneficiary.EncryptionKeyId = wrapped.KeyId;
            beneficiary.WrappedDataKey = wrapped.Value;
            beneficiary.IdentityEncrypted = AesGcmCipher.Encrypt(dataKey, identityPlain);
            beneficiary.ContactEncrypted = AesGcmCipher.Encrypt(dataKey, contactPlain);
            beneficiary.CitizenIdLast4 = data.CitizenId.Substring(data.CitizenId.Length - 4);
            beneficiary.CitizenIdHash = dataKeyProtector.ComputeBlindIndex("beneficiary-citizen-id", data.CitizenId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(identityPlain);
            CryptographicOperations.ZeroMemory(contactPlain);
        }
    }

    public BeneficiaryPersonalData Unprotect(Beneficiary beneficiary)
    {
        byte[] dataKey = dataKeyProtector.Unwrap(beneficiary.EncryptionKeyId, beneficiary.WrappedDataKey);
        byte[] identityPlain = Array.Empty<byte>();
        byte[] contactPlain = Array.Empty<byte>();
        try
        {
            identityPlain = AesGcmCipher.Decrypt(dataKey, beneficiary.IdentityEncrypted);
            contactPlain = AesGcmCipher.Decrypt(dataKey, beneficiary.ContactEncrypted);

            IdentityPayload? identity = JsonSerializer.Deserialize<IdentityPayload>(identityPlain, JsonOptions);
            ContactPayload? contact = JsonSerializer.Deserialize<ContactPayload>(contactPlain, JsonOptions);
            if (identity == null || contact == null)
            {
                throw new CryptographicException("Dữ liệu người nhận sau giải mã không hợp lệ.");
            }

            BeneficiaryPersonalData data = new BeneficiaryPersonalData();
            data.CitizenId = identity.CitizenId;
            data.FullName = identity.FullName;
            data.DateOfBirth = identity.DateOfBirth;
            data.Email = contact.Email;
            data.Phone = contact.Phone;
            return data;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(identityPlain);
            CryptographicOperations.ZeroMemory(contactPlain);
        }
    }


    /// <summary>Phần định danh trong IdentityEncrypted.</summary>
    private sealed class IdentityPayload
    {
        public string CitizenId { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly DateOfBirth { get; set; }
    }

    /// <summary>Phần liên hệ trong ContactEncrypted.</summary>
    private sealed class ContactPayload
    {
        public string? Email { get; set; }

        public string? Phone { get; set; }
    }
}
