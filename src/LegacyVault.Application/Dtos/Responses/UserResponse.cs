namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Thông tin người dùng trả cho FE. Không có PasswordHash (SEC-07).</summary>
public class UserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>"USER", "VERIFIER", "ADMIN".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>"ACTIVE", "LOCKED", "DISABLED".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>false → FE hiện "Hãy xác minh email" và nút gửi lại link (AUTH-04).</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Có mật khẩu app hay không (AUTH-03) — FE chọn hiện "Đổi mật khẩu" hay "Đặt mật khẩu".</summary>
    public bool HasPassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
