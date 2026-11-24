using System.Security.Cryptography;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope dữ liệu cá nhân của hồ sơ báo tin, dùng lại DataKeyProtector.
/// Giá trị lưu là JSON {k, w, c}: id KEK, DEK đã bọc và bản mã AES-256-GCM, đều dạng base64.
/// </summary>
public sealed class HandoverReportProtector : IHandoverReportProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly DataKeyProtector dataKeyProtector;

    public HandoverReportProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public string Protect(HandoverReportData data)
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

    public HandoverReportData Unprotect(string value)
    {
        Envelope? envelope = JsonSerializer.Deserialize<Envelope>(value, JsonOptions);
        if (envelope == null)
        {
            throw new CryptographicException("Hồ sơ báo tin không hợp lệ.");
        }
        byte[] dataKey = dataKeyProtector.Unwrap(envelope.K, Convert.FromBase64String(envelope.W));
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = AesGcmCipher.Decrypt(dataKey, Convert.FromBase64String(envelope.C));
            HandoverReportData? data = JsonSerializer.Deserialize<HandoverReportData>(plain, JsonOptions);
            if (data == null)
            {
                throw new CryptographicException("Hồ sơ báo tin sau giải mã không hợp lệ.");
            }
            return data;
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
