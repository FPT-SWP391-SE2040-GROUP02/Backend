using System.Security.Cryptography;
using System.Text;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần của mảnh khóa Người thực thi: sinh mảnh ngẫu nhiên 256 bit, định dạng dễ đọc, băm và kiểm.
/// Không đọc hay ghi DB, không ghi log mảnh (SEC-07).
/// </summary>
public static class ExecutorKeyRules
{
    /// <summary>Bảng chữ Crockford base32: bỏ I, L, O, U cho khỏi nhầm khi đọc hoặc chép tay.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int ShardBytes = 32;
    private const int GroupSize = 4;

    /// <summary>Sinh mảnh mới: 32 byte ngẫu nhiên mật mã, trả dạng "XXXX-XXXX-..." (13 nhóm).</summary>
    public static string GenerateShard()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(ShardBytes);
        string encoded = EncodeBase32(bytes);
        StringBuilder grouped = new StringBuilder();
        for (int index = 0; index < encoded.Length; index++)
        {
            if (index > 0 && index % GroupSize == 0)
            {
                grouped.Append('-');
            }
            grouped.Append(encoded[index]);
        }
        return grouped.ToString();
    }

    /// <summary>Chuẩn hóa mảnh người dùng gõ: bỏ gạch và khoảng trắng, in hoa, O thành 0, I và L thành 1.</summary>
    public static string Normalize(string? shard)
    {
        StringBuilder result = new StringBuilder();
        if (shard == null)
        {
            return string.Empty;
        }
        foreach (char raw in shard)
        {
            char letter = char.ToUpperInvariant(raw);
            if (letter == '-' || char.IsWhiteSpace(letter))
            {
                continue;
            }
            if (letter == 'O')
            {
                letter = '0';
            }
            else if (letter == 'I' || letter == 'L')
            {
                letter = '1';
            }
            result.Append(letter);
        }
        return result.ToString();
    }

    /// <summary>Băm mảnh gắn với kho và phiên bản, nên mảnh của kho khác hay phiên bản cũ không khớp.</summary>
    public static string Hash(Guid vaultId, int version, string shard)
    {
        string material = vaultId.ToString("D") + ":" + version.ToString() + ":" + Normalize(shard);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest);
    }

    /// <summary>true khi mảnh đưa ra khớp hash đã lưu (so sánh thời gian cố định).</summary>
    public static bool Matches(Guid vaultId, int version, string shard, string storedHash)
    {
        byte[] actual = Encoding.UTF8.GetBytes(Hash(vaultId, version, shard));
        byte[] expected = Encoding.UTF8.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string EncodeBase32(byte[] bytes)
    {
        StringBuilder result = new StringBuilder();
        int buffer = 0;
        int bits = 0;
        foreach (byte value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0)
        {
            result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }
        return result.ToString();
    }
}
