using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Băm mật khẩu bằng Argon2id theo chuẩn SEC-01 (OWASP).
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 19456; // 19 MiB
    private const int Iterations = 2;
    private const int Parallelism = 1;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Compute(password, salt, MemoryKb, Iterations, Parallelism, HashSize);
        return $"argon2id$m={MemoryKb},t={Iterations},p={Parallelism}" +
               $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != "argon2id")
        {
            return false;
        }

        try
        {
            var p = parts[1].Split(',').Select(x => int.Parse(x.Substring(2))).ToArray();
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Compute(password, salt, p[0], p[1], p[2], expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKb, int iterations, int parallelism, int size)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(size);
    }
}
