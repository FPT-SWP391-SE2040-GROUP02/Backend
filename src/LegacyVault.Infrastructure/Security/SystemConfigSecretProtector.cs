using System.Security.Cryptography;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope các bí mật của cấu hình dịch vụ ngoài, dùng lại DataKeyProtector của tài sản.
/// </summary>
/// <remarks>
/// - Mỗi lần Protect sinh một DEK mới (32 byte); DB chỉ lưu DEK đã bọc bằng KEK.
/// - Bản rõ là JSON {tên: giá trị}, mã hóa bằng AES-256-GCM (nonce + ciphertext + tag).
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class SystemConfigSecretProtector : ISystemConfigSecretProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly DataKeyProtector dataKeyProtector;

    public SystemConfigSecretProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(SystemConfig row, IReadOnlyDictionary<string, string> secrets)
    {
        if (secrets.Count == 0)
        {
            row.EncryptionKeyId = string.Empty;
            row.WrappedDataKey = Array.Empty<byte>();
            row.SecretsEncrypted = Array.Empty<byte>();
            return;
        }

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(secrets, JsonOptions);
            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            row.EncryptionKeyId = wrapped.KeyId;
            row.WrappedDataKey = wrapped.Value;
            row.SecretsEncrypted = AesGcmCipher.Encrypt(dataKey, plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Dictionary<string, string> Unprotect(SystemConfig row)
    {
        if (row.SecretsEncrypted.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        byte[] dataKey = dataKeyProtector.Unwrap(row.EncryptionKeyId, row.WrappedDataKey);
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = AesGcmCipher.Decrypt(dataKey, row.SecretsEncrypted);
            Dictionary<string, string>? secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(plain, JsonOptions);
            if (secrets == null)
            {
                throw new CryptographicException("Giá trị bí mật của cấu hình sau giải mã không hợp lệ.");
            }
            return secrets;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
