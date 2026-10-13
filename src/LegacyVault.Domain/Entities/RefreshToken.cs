namespace LegacyVault.Domain.Entities;

/// <summary>
/// Refresh token của một phiên đăng nhập (bảng RefreshTokens).
/// </summary>
/// <remarks>
/// - Chỉ lưu HASH; token gốc chỉ nằm trong cookie của trình duyệt (SEC-07).
/// - Xoay vòng: mỗi lần refresh thu hồi token cũ, tạo token mới cùng FamilyId (một family = một lần đăng nhập
///   trên một thiết bị). Dùng lại token đã thu hồi → thu hồi cả family (dấu hiệu bị đánh cắp).
/// - NFR-06: phiên hết khi không dùng 30 phút (idle) hoặc quá 12 giờ từ SessionStartedAt (tối đa), kể cả khi xoay vòng.
/// </remarks>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 của token gốc, dạng hex. Duy nhất.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public Guid FamilyId { get; set; }

    /// <summary>Lúc đăng nhập mở phiên này; không đổi khi xoay vòng (để tính mức tối đa 12 giờ).</summary>
    public DateTimeOffset SessionStartedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>"Ghi nhớ đăng nhập": cookie sống qua khi tắt trình duyệt.</summary>
    public bool IsPersistent { get; set; }

    /// <summary>null = còn hiệu lực.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Token thay thế khi xoay vòng; null nếu bị thu hồi do đăng xuất/khóa.</summary>
    public Guid? ReplacedByTokenId { get; set; }
}
