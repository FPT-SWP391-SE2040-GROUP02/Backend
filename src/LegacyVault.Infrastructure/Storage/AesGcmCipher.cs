using System.Security.Cryptography;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Mã hóa/giải mã một mảng byte bằng AES-256-GCM. Kết quả = nonce (12 byte) + ciphertext + tag (16 byte).
/// </summary>
/// <remarks>
/// Nonce sinh ngẫu nhiên mỗi lần mã hóa. Dữ liệu bị sửa hoặc sai khóa → CryptographicException khi giải mã.
/// </remarks>
public static class AesGcmCipher
{
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static byte[] Encrypt(byte[] key, byte[] plainText)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipherText = new byte[plainText.Length];
        byte[] tag = new byte[TagSize];

        using (AesGcm aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plainText, cipherText, tag);
        }

        byte[] result = new byte[NonceSize + cipherText.Length + TagSize];
        nonce.CopyTo(result, 0);
        cipherText.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + cipherText.Length);
        return result;
    }

    public static byte[] Decrypt(byte[] key, byte[] encrypted)
    {
        if (encrypted.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Dữ liệu đã mã hóa có độ dài không hợp lệ.");
        }

        int cipherLength = encrypted.Length - NonceSize - TagSize;
        byte[] nonce = encrypted.AsSpan(0, NonceSize).ToArray();
        byte[] cipherText = encrypted.AsSpan(NonceSize, cipherLength).ToArray();
        byte[] tag = encrypted.AsSpan(NonceSize + cipherLength, TagSize).ToArray();
        byte[] plainText = new byte[cipherLength];

        using (AesGcm aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(nonce, cipherText, tag, plainText);
        }
        return plainText;
    }
}
