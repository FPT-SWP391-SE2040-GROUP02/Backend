namespace LegacyVault.Domain.Entities;

/// <summary>Mã khôi phục dùng một lần khi Admin mất ứng dụng xác thực. DB chỉ lưu hash.</summary>
public class TwoFactorRecoveryCode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}
