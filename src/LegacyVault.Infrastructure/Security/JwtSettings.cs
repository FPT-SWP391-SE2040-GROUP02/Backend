namespace LegacyVault.Infrastructure.Security;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "LegacyVault";

    public string Audience { get; set; } = "LegacyVault";

    public string SigningKey { get; set; } = "LegacyVault_DevSecretKey_AtLeast32BytesLong!";

    public int AccessTokenMinutes { get; set; } = 15;
}
