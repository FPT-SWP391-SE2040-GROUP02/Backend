using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>Băm và xác thực mật khẩu bằng Argon2id.</summary>
/// <remarks>
/// Giữ định dạng hash cũ của dự án và chỉ chấp nhận bộ tham số hiện tại.
/// Mỗi lần tính hash dùng instance riêng để an toàn khi đăng ký singleton.
/// Khi đổi bộ tham số phải bổ sung hỗ trợ hash cũ trước khi triển khai.
/// </remarks>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;

    private const int PartCount = 4;
    private static readonly int SaltBase64Length = Base64.GetMaxEncodedToUtf8Length(SaltSize);
    private static readonly int HashBase64Length = Base64.GetMaxEncodedToUtf8Length(HashSize);

    private const string Algorithm = "argon2id";
    private static readonly string Parameters = FormattableString.Invariant(
        $"m={MemoryKb},t={Iterations},p={Parallelism}");
    private const char Separator = '$';

    private static readonly int EncodedHashLength =
        Algorithm.Length + Parameters.Length +
        SaltBase64Length + HashBase64Length + PartCount - 1;

    /// <summary>Tạo hash mật khẩu với salt ngẫu nhiên.</summary>
    /// <param name="password">Mật khẩu nguyên bản, không trim hoặc đổi casing.</param>
    /// <returns>Chuỗi chứa thuật toán, tham số, salt và hash.</returns>
    /// <exception cref="ArgumentNullException">Mật khẩu là null.</exception>
    /// <exception cref="ArgumentException">Mật khẩu rỗng hoặc chỉ có khoảng trắng.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Mật khẩu vượt giới hạn đầu vào.</exception>
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            password.Length,
            128,
            nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Compute(password, salt);

        try
        {
            return string.Join(
                Separator,
                Algorithm,
                Parameters,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    /// <summary>Xác thực mật khẩu bằng so sánh thời gian cố định.</summary>
    /// <param name="password">Mật khẩu cần kiểm tra, không chuẩn hóa.</param>
    /// <param name="passwordHash">Chuỗi hash được lưu trong database.</param>
    /// <returns>True khi mật khẩu khớp; false khi đầu vào hoặc hash không hợp lệ.</returns>
    /// <remarks>
    /// Hash sai định dạng bị từ chối trước khi chạy Argon2.
    /// Lỗi tài nguyên hoặc lỗi hệ thống được truyền lên tầng xử lý lỗi.
    /// </remarks>
    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length > 128 ||
            string.IsNullOrEmpty(passwordHash) ||
            passwordHash.Length != EncodedHashLength)
        {
            return false;
        }

        string[] parts = passwordHash.Split(Separator);

        if (parts.Length != PartCount ||
            parts[0] != Algorithm ||
            parts[1] != Parameters ||
            parts[2].Length != SaltBase64Length ||
            parts[3].Length != HashBase64Length)
        {
            return false;
        }

        byte[] salt = new byte[SaltSize];
        byte[] expected = new byte[HashSize];

        try
        {
            if (!Convert.TryFromBase64String(
                    parts[2], salt, out int saltWritten) ||
                saltWritten != SaltSize ||
                !Convert.TryFromBase64String(
                    parts[3], expected, out int hashWritten) ||
                hashWritten != HashSize)
            {
                return false;
            }

            // Chỉ nhận Base64 đúng định dạng do Hash() tạo ra.
            if (Convert.ToBase64String(salt) != parts[2] ||
                Convert.ToBase64String(expected) != parts[3])
            {
                return false;
            }

            byte[] actual = Compute(password, salt);

            try
            {
                return CryptographicOperations.FixedTimeEquals(
                    actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    /// <summary>Tính Argon2id bằng bộ tham số cố định của dự án.</summary>
    /// <param name="password">Mật khẩu nguyên bản.</param>
    /// <param name="salt">Salt đã được kiểm tra độ dài.</param>
    /// <returns>Hash nhị phân; bên gọi chịu trách nhiệm xóa buffer sau khi dùng.</returns>
    private static byte[] Compute(string password, byte[] salt)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = MemoryKb,
                Iterations = Iterations,
                DegreeOfParallelism = Parallelism
            };

            return argon2.GetBytes(HashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
