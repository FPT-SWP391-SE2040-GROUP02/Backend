using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtSettings> options, TimeProvider timeProvider)
    {
        _settings = options.Value;
        _timeProvider = timeProvider;
    }

    public AccessToken CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Id == Guid.Empty || !Enum.IsDefined(user.Role))
        {
            throw new ArgumentException("User ID hoặc role không hợp lệ.", nameof(user));
        }
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            // sub là Id người dùng: CurrentUser đọc lại claim này ở mỗi request để biết ai đang gọi.
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role.ToString().ToUpperInvariant()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                CreateSigningKey(_settings.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken { Value = token, ExpiresAt = expiresAt };
    }

    public OpaqueToken CreateOpaqueToken()
    {
        // Dùng cho refresh token và các link một lần: chuỗi gốc gửi cho người dùng, DB chỉ lưu SHA-256 của chuỗi.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Convert.ToHexString(bytes).ToLowerInvariant();
        var hash = HashOpaqueToken(value);
        return new OpaqueToken { Value = value, Hash = hash };
    }

    public string HashOpaqueToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static SymmetricSecurityKey CreateSigningKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new ArgumentException("Khóa ký JWT không đủ số byte UTF-8.", nameof(key));
        }
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }
}
