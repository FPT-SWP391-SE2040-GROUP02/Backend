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

    /// <summary>Policy giới hạn đăng ký tài khoản (chống dò email/số điện thoại đã dùng qua lỗi 409 và chống dội email xác minh).</summary>
    public const string Register = "auth-register";

    /// <summary>Số lượt đăng ký trong mỗi cửa sổ cho một IP.</summary>
    public const int RegisterPermitLimit = 10;

    /// <summary>Policy giới hạn phát nonce đăng nhập Google (mỗi lần mở trang có nút Google lấy một nonce).</summary>
    public const string GoogleNonce = "auth-google-nonce";

    /// <summary>Số nonce được phát trong mỗi cửa sổ cho một IP.</summary>
    public const int GoogleNoncePermitLimit = 30;

    /// <summary>Policy giới hạn thao tác eKYC tốn tài nguyên (đọc giấy tờ, bắt đầu và chốt thử thách) theo từng người dùng.</summary>
    public const string Ekyc = "ekyc";

    /// <summary>Số lượt thao tác eKYC tốn tài nguyên trong mỗi cửa sổ cho một người dùng.</summary>
    public const int EkycPermitLimit = 30;

    /// <summary>Policy giới hạn gửi khung hình thử thách camera theo từng người dùng.</summary>
    public const string EkycFrame = "ekyc-frame";

    /// <summary>Khung hình gửi liên tục trong thử thách nên giới hạn cao hơn (dịch vụ eKYC tự chặn quá 400 khung/phiên).</summary>
    public const int EkycFramePermitLimit = 600;
}