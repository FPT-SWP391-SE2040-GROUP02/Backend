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

    /// <summary>Claim định danh phiên đăng nhập (family) của Access Token.</summary>
    public const string FamilyIdClaim = "family_id";

    public AccessToken CreateAccessToken(User user, Guid? familyId = null)
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
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role.ToString().ToUpperInvariant()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (familyId.HasValue && familyId.Value != Guid.Empty)
        {
            claims.Add(new Claim(FamilyIdClaim, familyId.Value.ToString("D")));
        }

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

    public AccessToken CreateSmartwatchToken(User user, Guid vaultId, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_settings.AccessTokenMinutes);

        // R2 (audit 2026-10-27): token thiết bị cố tình KHÔNG dùng role của owner.
        // Endpoint /check-ins/smartwatch chấp nhận role "SmartwatchDevice" này và
        // xác minh vault_id claim khớp vault của owner; mọi endpoint khác (vẫn
        // [Authorize] theo role owner/admin) từ chối token thiết bị.
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", SmartwatchRoleClaim),
            new Claim("scope", "check-in:write"),
            new Claim("vault_id", vaultId.ToString("D")),
            new Claim("device_id", deviceId ?? string.Empty),
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

    /// <summary>Giá trị role claim dành riêng cho token smartwatch (không phải UserRole).</summary>
    public const string SmartwatchRoleClaim = "SmartwatchDevice";

    public OpaqueToken CreateOpaqueToken()
    {
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
