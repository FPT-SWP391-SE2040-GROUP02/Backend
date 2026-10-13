namespace LegacyVault.Application.Security;

/// <summary>Access token vừa sinh và thời điểm hết hạn.</summary>
public class AccessToken
{
    public string Value { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
