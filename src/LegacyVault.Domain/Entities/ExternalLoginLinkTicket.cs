using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Vé chờ liên kết Google vào tài khoản app có sẵn (bảng ExternalLoginLinkTickets), AUTH-02.
/// </summary>
/// <remarks>
/// Đăng nhập Google mà email trùng tài khoản app CHƯA liên kết thì không tự gộp: backend tạo vé này và yêu cầu
/// người dùng nhập mật khẩu tài khoản app; đúng mật khẩu mới tạo ExternalLogin.
/// Chỉ lưu HASH; vé sống ngắn (gợi ý 10 phút) và dùng một lần.
/// </remarks>
public class ExternalLoginLinkTicket
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Tài khoản app sẽ được liên kết.</summary>
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public LoginProvider Provider { get; set; }

    public string ProviderSubject { get; set; } = string.Empty;

    public string? ProviderEmail { get; set; }

    /// <summary>SHA-256 của vé gốc, dạng hex.</summary>
    public string TicketHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}
