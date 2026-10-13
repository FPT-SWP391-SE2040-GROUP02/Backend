namespace LegacyVault.Domain.Entities;

/// <summary>
/// Link xác minh email gửi cho người vừa đăng ký (bảng EmailVerificationTokens, AUTH-04).
/// </summary>
/// <remarks>
/// - Chỉ lưu HASH; token gốc chỉ nằm trong email.
/// - Dùng một lần (UsedAt khác null = đã dùng). Gửi lại link thì các link cũ chưa dùng bị vô hiệu.
/// </remarks>
public class EmailVerificationToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 của token gốc, dạng hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Thời điểm đã dùng hoặc bị thay bằng link mới; null = chưa dùng.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
