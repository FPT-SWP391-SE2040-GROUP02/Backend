namespace LegacyVault.Domain.Entities;

/// <summary>
/// Link đặt lại mật khẩu gửi qua email khi người dùng quên mật khẩu (bảng PasswordResetTokens).
/// </summary>
/// <remarks>
/// - Chỉ lưu mã băm; token gốc chỉ nằm trong email.
/// - Dùng một lần, hạn ngắn. Yêu cầu link mới thì các link cũ chưa dùng bị vô hiệu.
/// </remarks>
public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 của token gốc, dạng hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Thời điểm đã dùng hoặc bị thay bằng link mới; null là chưa dùng.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
