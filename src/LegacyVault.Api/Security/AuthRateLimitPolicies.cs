namespace LegacyVault.Api.Security;

/// <summary>Tên và giới hạn các policy auth theo địa chỉ IP.</summary>
public static class AuthRateLimitPolicies
{
    /// <summary>Policy giới hạn đăng nhập.</summary>
    public const string Login = "auth-login";

    /// <summary>Policy giới hạn làm mới phiên.</summary>
    public const string Refresh = "auth-refresh";

    /// <summary>Policy giới hạn đăng xuất.</summary>
    public const string Logout = "auth-logout";

    /// <summary>Số lượt đăng nhập trong mỗi cửa sổ.</summary>
    public const int LoginPermitLimit = 5;

    /// <summary>Số lượt refresh hoặc logout trong mỗi cửa sổ riêng.</summary>
    public const int SessionPermitLimit = 30;

    /// <summary>Không xếp hàng request vượt giới hạn.</summary>
    public const int QueueLimit = 0;

    /// <summary>Độ dài cửa sổ giới hạn.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Policy giới hạn quên mật khẩu và đặt lại mật khẩu (chống dội email và dò token).</summary>
    public const string PasswordReset = "auth-password-reset";

    /// <summary>Số lượt quên/đặt lại mật khẩu trong mỗi cửa sổ.</summary>
    public const int PasswordResetPermitLimit = 5;
}