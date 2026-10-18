namespace LegacyVault.Infrastructure.Security;

/// <summary>Cấu hình mục "Jwt" trong appsettings, dùng để cấp và kiểm access token.</summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
 
    public string Audience { get; set; } = string.Empty;

    /// <summary>Khóa bí mật ký token, tối thiểu 32 byte. Môi trường thật đặt bằng biến môi trường, không ghi vào appsettings.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}
