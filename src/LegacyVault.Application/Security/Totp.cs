using System.Security.Cryptography;
using System.Text;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã dùng một lần theo thời gian (TOTP, RFC 6238): HMAC-SHA1, 6 chữ số, bước 30 giây,
/// tương thích Google Authenticator / Authy.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;
    public const int SecretBytes = 20;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Số thứ tự bước 30 giây tính từ 1970-01-01 UTC.</summary>
    public static long StepAt(DateTimeOffset time)
    {
        return time.ToUnixTimeSeconds() / StepSeconds;
    }

    /// <summary>Mã 6 chữ số của một bước (RFC 4226 dynamic truncation).</summary>
    public static string ComputeCode(byte[] secret, long step)
    {
        byte[] counter = new byte[8];
        long value = step;
        for (int i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(value & 0xFF);
            value = value >> 8;
        }

        byte[] hash = HMACSHA1.HashData(secret, counter);
        int offset = hash[hash.Length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        int modulo = 1;
        for (int i = 0; i < Digits; i++)
        {
            modulo = modulo * 10;
        }
        int otp = binary % modulo;
        return otp.ToString().PadLeft(Digits, '0');
    }

    /// <summary>
    /// Kiểm tra mã trong cửa sổ ±1 bước (lệch đồng hồ). Trả step khớp, hoặc null nếu sai.
    /// Chỉ chấp nhận step lớn hơn lastUsedStep để một mã không dùng được hai lần.
    /// </summary>
    public static long? Verify(byte[] secret, string code, DateTimeOffset now, long lastUsedStep)
    {
        if (code.Length != Digits)
        {
            return null;
        }

        long current = StepAt(now);
        long? matched = null;
        for (long step = current - 1; step <= current + 1; step++)
        {
            string expected = ComputeCode(secret, step);
            // So sánh thời gian không đổi và luôn duyệt hết 3 bước để không lộ bước nào khớp.
            bool same = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code));
            if (same && step > lastUsedStep)
            {
                matched = step;
            }
        }
        return matched;
    }

    public static byte[] NewSecret()
    {
        return RandomNumberGenerator.GetBytes(SecretBytes);
    }

    public static string ToBase32(byte[] data)
    {
        StringBuilder builder = new StringBuilder();
        int buffer = 0;
        int bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                int index = (buffer >> (bits - 5)) & 0x1F;
                builder.Append(Base32Alphabet[index]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            int index = (buffer << (5 - bits)) & 0x1F;
            builder.Append(Base32Alphabet[index]);
        }
        return builder.ToString();
    }
}
