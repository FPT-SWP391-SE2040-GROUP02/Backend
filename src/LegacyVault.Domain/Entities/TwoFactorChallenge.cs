namespace LegacyVault.Domain.Entities;

/// <summary>
/// Thử thách bước hai sau khi mật khẩu/Google đã đúng nhưng Admin chưa nhập mã TOTP. Chưa cấp phiên.
/// Token chỉ lưu hash, sống ngắn, dùng một lần, sai quá giới hạn thì bị hủy.
/// </summary>
public class TwoFactorChallenge
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}
