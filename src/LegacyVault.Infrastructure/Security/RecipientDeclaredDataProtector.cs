using System.Security.Cryptography;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope số căn cước và ngày sinh người nhận khai trên biên bản, dùng lại DataKeyProtector.
/// Giá trị lưu là JSON {k, w, c}: id KEK, DEK đã bọc và bản mã AES-256-GCM, đều dạng base64.
/// </summary>
public sealed class RecipientDeclaredDataProtector : IRecipientDeclaredDataProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly DataKeyProtector dataKeyProtector;

    public RecipientDeclaredDataProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public string Protect(RecipientDeclaredData data)
    {
        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            Envelope envelope = new Envelope();
            envelope.K = wrapped.KeyId;
            envelope.W = Convert.ToBase64String(wrapped.Value);
            envelope.C = Convert.ToBase64String(AesGcmCipher.Encrypt(dataKey, plain));
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public RecipientDeclaredData? Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }
        Envelope? envelope = JsonSerializer.Deserialize<Envelope>(protectedValue, JsonOptions);
        if (envelope == null)
        {
            throw new CryptographicException("Dữ liệu người nhận khai không hợp lệ.");
        }
        byte[] dataKey = dataKeyProtector.Unwrap(envelope.K, Convert.FromBase64String(envelope.W));
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = AesGcmCipher.Decrypt(dataKey, Convert.FromBase64String(envelope.C));
            return JsonSerializer.Deserialize<RecipientDeclaredData>(plain, JsonOptions)
                ?? throw new CryptographicException("Dữ liệu người nhận khai sau giải mã không hợp lệ.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private sealed class Envelope
    {
        public string K { get; set; } = string.Empty;

        public string W { get; set; } = string.Empty;

        public string C { get; set; } = string.Empty;
    }
}
