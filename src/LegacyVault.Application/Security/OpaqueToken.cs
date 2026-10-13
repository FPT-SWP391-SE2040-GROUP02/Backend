namespace LegacyVault.Application.Security;

/// <summary>Token ngẫu nhiên vừa sinh: Value gửi cho người dùng (cookie/email), Hash lưu DB (SEC-07).</summary>
public class OpaqueToken
{
    public string Value { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}
