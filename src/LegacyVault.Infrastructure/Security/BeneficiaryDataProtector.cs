using System.Security.Cryptography;
using System.Text;
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
/// - AAD = "beneficiary:{Id}:identity" hoặc "beneficiary:{Id}:contact": chép cột sang hồ sơ khác
///   hoặc đổi chỗ hai cột thì giải mã thất bại.
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class BeneficiaryDataProtector : IBeneficiaryDataProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
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
            beneficiary.IdentityEncrypted = Encrypt(dataKey, identityPlain, BuildAad(beneficiary.Id, IdentityPart));
            beneficiary.ContactEncrypted = Encrypt(dataKey, contactPlain, BuildAad(beneficiary.Id, ContactPart));
            beneficiary.CitizenIdLast4 = data.CitizenId.Substring(data.CitizenId.Length - 4);
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
            identityPlain = Decrypt(dataKey, beneficiary.IdentityEncrypted, BuildAad(beneficiary.Id, IdentityPart));
            contactPlain = Decrypt(dataKey, beneficiary.ContactEncrypted, BuildAad(beneficiary.Id, ContactPart));

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

    private static byte[] BuildAad(Guid beneficiaryId, string part)
    {
        return Encoding.UTF8.GetBytes($"beneficiary:{beneficiaryId}:{part}");
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
            throw new CryptographicException("Dữ liệu người nhận đã mã hóa có độ dài không hợp lệ.");
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
