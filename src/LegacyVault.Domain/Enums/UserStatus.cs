namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái tài khoản (SRS §11.1). Email đã xác minh hay chưa lưu riêng ở User.EmailVerifiedAt.
/// </summary>
public enum UserStatus
{
    /// <summary>Được đăng nhập.</summary>
    Active = 0,
    /// <summary>Bị khóa tạm (ví dụ do quản trị/sự cố); chặn thao tác mới và phiên liên quan (ROLE-04).</summary>
    Locked = 1,
    /// <summary>Bị vô hiệu hóa.</summary>
    Disabled = 2,
}
