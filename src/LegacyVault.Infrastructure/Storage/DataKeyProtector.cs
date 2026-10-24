using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Tạo DEK (khóa mã hóa nội dung) và bọc/mở DEK bằng KEK lấy từ cấu hình — phần "envelope" của mã hóa envelope.
/// </summary>
/// <remarks>
/// - Mỗi phiên bản nội dung có một DEK ngẫu nhiên 32 byte riêng (AES-256).
/// - DEK được bọc bằng AES-256-GCM với KEK; kết quả = nonce (12 byte) + DEK đã mã hóa (32 byte) + tag (16 byte).
///   Id của KEK được đưa vào AAD, nên DEK bọc bằng khóa này không mở được bằng khóa khác.
/// - Chỉ DEK đã bọc được lưu vào DB; DEK gốc chỉ tồn tại trong RAM lúc mã hóa/giải mã và được xóa sau khi dùng.
/// - Sau này chuyển sang KMS (AWS KMS, Azure Key Vault...) thì chỉ thay lớp này.
/// </remarks>
public sealed partial class DataKeyProtector
{
    public const int KeySize = 32;
    public const int WrappedKeySize = NonceSize + KeySize + TagSize;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string activeKeyId;
    private readonly Dictionary<string, byte[]> keys;
    private readonly byte[] blindIndexKey;

    public DataKeyProtector(IOptions<AssetEncryptionSettings> options)
    {
        AssetEncryptionSettings settings = options.Value;
        string? error = FindConfigurationError(settings);
        if (error != null)
        {
            throw new InvalidOperationException(error);
        }

        activeKeyId = settings.ActiveKeyId;
        keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in settings.Keys)
        {
            keys[entry.Key] = Convert.FromBase64String(entry.Value);
        }

        if (string.IsNullOrWhiteSpace(settings.BlindIndexKey))
        {
            blindIndexKey = System.Security.Cryptography.HKDF.DeriveKey(
                System.Security.Cryptography.HashAlgorithmName.SHA256, keys[activeKeyId], KeySize,
                salt: null, info: System.Text.Encoding.UTF8.GetBytes("legacyvault:citizen-id-blind-index"));
        }
        else
        {
            blindIndexKey = Convert.FromBase64String(settings.BlindIndexKey);
        }
    }

    /// <summary>
    /// Kiểm tra cấu hình khi khởi động. Trả câu báo lỗi (không chứa giá trị khóa), hợp lệ thì trả null.
    /// </summary>
    public static string? FindConfigurationError(AssetEncryptionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ActiveKeyId))
        {
            return "Thiếu AssetEncryption:ActiveKeyId.";
        }
        if (!settings.Keys.ContainsKey(settings.ActiveKeyId))
        {
            return "AssetEncryption:Keys không có khóa trùng với ActiveKeyId.";
        }
        if (!string.IsNullOrWhiteSpace(settings.BlindIndexKey) && !IsBase64KeyOfCorrectSize(settings.BlindIndexKey))
        {
            return $"AssetEncryption:BlindIndexKey phải là base64 của đúng {KeySize} byte.";
        }
        foreach (KeyValuePair<string, string> entry in settings.Keys)
        {
            if (!KeyIdPattern().IsMatch(entry.Key))
            {
                return $"Id khóa '{entry.Key}' không hợp lệ: chỉ dùng a-z, 0-9, dấu '-', tối đa 64 ký tự.";
            }
            if (!IsBase64KeyOfCorrectSize(entry.Value))
            {
                return $"Khóa '{entry.Key}' phải là base64 của đúng {KeySize} byte.";
            }
        }
        return null;
    }

    /// <summary>HMAC-SHA256 của giá trị chuẩn hóa bằng khóa chỉ mục chống trùng; luôn 32 byte, không đảo ngược được.</summary>
    public byte[] ComputeBlindIndex(string purpose, string value)
    {
        byte[] message = System.Text.Encoding.UTF8.GetBytes(purpose + ":" + value);
        return System.Security.Cryptography.HMACSHA256.HashData(blindIndexKey, message);
    }

    /// <summary>Tạo một DEK ngẫu nhiên mới. Người gọi phải xóa mảng (CryptographicOperations.ZeroMemory) sau khi dùng.</summary>
    public byte[] CreateDataKey()
    {
        return RandomNumberGenerator.GetBytes(KeySize);
    }

    /// <summary>Bọc DEK bằng KEK đang hoạt động.</summary>
    public WrappedDataKey Wrap(byte[] dataKey)
    {
        if (dataKey.Length != KeySize)
        {
            throw new ArgumentException("DEK phải dài 32 byte.", nameof(dataKey));
        }

        byte[] result = new byte[WrappedKeySize];
        Span<byte> nonce = result.AsSpan(0, NonceSize);
        Span<byte> cipherText = result.AsSpan(NonceSize, KeySize);
        Span<byte> tag = result.AsSpan(NonceSize + KeySize, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using (AesGcm aes = new AesGcm(keys[activeKeyId], TagSize))
        {
            aes.Encrypt(nonce, dataKey, cipherText, tag, Encoding.ASCII.GetBytes(activeKeyId));
        }

        WrappedDataKey wrapped = new WrappedDataKey();
        wrapped.KeyId = activeKeyId;
        wrapped.Value = result;
        return wrapped;
    }

    /// <summary>
    /// Mở DEK đã bọc. Người gọi phải xóa mảng trả về sau khi dùng.
    /// Ném CryptographicException nếu không có KEK với id này hoặc DEK đã bọc bị sửa.
    /// </summary>
    public byte[] Unwrap(string keyId, byte[] wrappedKey)
    {
        byte[]? keyEncryptionKey;
        if (!keys.TryGetValue(keyId, out keyEncryptionKey))
        {
            throw new CryptographicException($"Không có KEK với id '{keyId}' trong cấu hình AssetEncryption:Keys.");
        }
        if (wrappedKey.Length != WrappedKeySize)
        {
            throw new CryptographicException("DEK đã bọc có độ dài không hợp lệ.");
        }

        byte[] dataKey = new byte[KeySize];
        using (AesGcm aes = new AesGcm(keyEncryptionKey, TagSize))
        {
            aes.Decrypt(
                wrappedKey.AsSpan(0, NonceSize),
                wrappedKey.AsSpan(NonceSize, KeySize),
                wrappedKey.AsSpan(NonceSize + KeySize, TagSize),
                dataKey,
                Encoding.ASCII.GetBytes(keyId));
        }
        return dataKey;
    }

    private static bool IsBase64KeyOfCorrectSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        byte[] buffer = new byte[KeySize + 1];
        int written;
        if (!Convert.TryFromBase64String(value, buffer, out written))
        {
            return false;
        }
        return written == KeySize;
    }

    [GeneratedRegex("^[a-z0-9-]{1,64}$")]
    private static partial Regex KeyIdPattern();
}

/// <summary>DEK đã bọc và id của KEK đã dùng để bọc; cả hai được lưu vào Asset.</summary>
public sealed class WrappedDataKey
{
    public string KeyId { get; set; } = string.Empty;

    public byte[] Value { get; set; } = Array.Empty<byte>();
}
