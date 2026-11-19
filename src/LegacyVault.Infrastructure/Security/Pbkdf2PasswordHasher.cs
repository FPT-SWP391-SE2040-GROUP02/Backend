using System.Security.Cryptography;
using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Băm mật khẩu bằng PBKDF2-HMAC-SHA256, số vòng ghi ngay trong chuỗi lưu để sau này tăng được mà không hỏng hash cũ.
/// </summary>
/// <remarks>
/// Định dạng mới: <c>pbkdf2$&lt;vòng&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>. Hash cũ <c>salt$hash</c> (SHA-256 một vòng,
/// <see cref="Sha256PasswordHasher"/>) vẫn xác thực được; <see cref="NeedsRehash"/> báo true để nơi đăng nhập băm lại.
/// So sánh thời gian cố định. Chuỗi sai định dạng trả false, không ném lỗi.
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string Prefix = "pbkdf2";
    public const int DefaultIterations = 600_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    // Chặn chuỗi trong DB bị sửa thành số vòng khổng lồ để làm treo máy chủ.
    private const int MaxIterations = 10_000_000;

    private readonly int _iterations;
    private readonly Sha256PasswordHasher _legacy = new Sha256PasswordHasher();

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 1 || iterations > MaxIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }
        if (!passwordHash.StartsWith(Prefix + "$", StringComparison.Ordinal))
        {
            return _legacy.Verify(password, passwordHash);
        }

        if (!TryParse(passwordHash, out int iterations, out byte[] salt, out byte[] expected))
        {
            return false;
        }
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string passwordHash)
    {
        if (!TryParse(passwordHash, out int iterations, out _, out _))
        {
            return true;
        }
        return iterations < _iterations;
    }

    private static bool TryParse(string passwordHash, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();
        string[] parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix
            || !int.TryParse(parts[1], out iterations) || iterations < 1 || iterations > MaxIterations)
        {
            return false;
        }
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length > 0 && hash.Length > 0;
    }
}
