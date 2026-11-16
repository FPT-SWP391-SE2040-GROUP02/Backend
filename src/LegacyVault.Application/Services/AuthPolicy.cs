namespace LegacyVault.Application.Services;

/// <summary>
/// Các con số của luật đăng nhập dùng chung: khóa tạm khi sai mật khẩu và thời hạn phiên.
/// Đặt một chỗ để AuthService, LoginThrottleGuard và LoginSessionIssuer luôn khớp nhau.
/// </summary>
public static class AuthPolicy
{
    /// <summary>Số lần sai mật khẩu trong một cửa sổ thì bị khóa tạm.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Thời gian khóa tạm sau khi sai quá số lần cho phép.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Cửa sổ đếm số lần sai, tính từ lần sai đầu tiên.</summary>
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    /// <summary>Phiên hết hạn nếu không hoạt động trong khoảng này.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Phiên hết hạn sau khoảng này kể từ lúc đăng nhập, dù vẫn hoạt động.</summary>
    public static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromHours(12);
}
