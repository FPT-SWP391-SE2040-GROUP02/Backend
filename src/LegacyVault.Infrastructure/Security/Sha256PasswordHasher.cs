using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>Băm mật khẩu bằng SHA-256 kèm salt ngẫu nhiên.</summary>
/// <remarks>
/// Chuỗi lưu trong DB có dạng <c>salt$hash</c>, với hash = SHA-256(salt + mật khẩu).
/// Mỗi mật khẩu có salt riêng nên hai người cùng mật khẩu vẫn ra hash khác nhau.
/// </remarks>
public sealed class Sha256PasswordHasher : IPasswordHasher
{
    /// <summary>Tạo salt ngẫu nhiên rồi băm mật khẩu.</summary>
    public string Hash(string password)
    {
        string salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        return salt + "$" + ComputeHash(password, salt);
    }

    /// <summary>Lấy salt trong chuỗi đã lưu, băm lại mật khẩu nhập vào và so với hash đã lưu.</summary>
    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        string[] parts = passwordHash.Split('$');
        if (parts.Length != 2)
        {
            return false;
        }

        string salt = parts[0];
        string savedHash = parts[1];
        return ComputeHash(password, salt) == savedHash;
    }

    /// <summary>SHA-256(salt + mật khẩu), trả về dạng base64.</summary>
    private static string ComputeHash(string password, string salt)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(salt + password);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
