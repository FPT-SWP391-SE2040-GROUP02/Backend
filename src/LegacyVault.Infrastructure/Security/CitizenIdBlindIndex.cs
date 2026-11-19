using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>HMAC-SHA256(khóa riêng, số CCCD) dạng hex. Khóa base64, tối thiểu 32 byte.</summary>
public sealed class CitizenIdBlindIndex : ICitizenIdBlindIndex
{
    private const int MinKeyBytes = 32;

    private readonly byte[]? key;

    public CitizenIdBlindIndex(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return;
        }
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Ekyc:CitizenIdIndexKey phải là chuỗi base64.");
        }
        if (decoded.Length < MinKeyBytes)
        {
            throw new InvalidOperationException("Ekyc:CitizenIdIndexKey phải dài ít nhất 32 byte.");
        }
        key = decoded;
    }

    public bool Enabled => key != null;

    public string? Compute(string citizenId)
    {
        if (key == null)
        {
            return null;
        }
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(citizenId)));
    }
}
