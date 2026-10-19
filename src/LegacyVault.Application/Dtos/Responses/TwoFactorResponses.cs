namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái xác thực hai bước của tài khoản đang đăng nhập.</summary>
public class TwoFactorStatusResponse
{
    public bool Enabled { get; set; }

    /// <summary>Đã gọi setup nhưng chưa xác nhận bằng mã đầu tiên.</summary>
    public bool SetupPending { get; set; }

    public int RecoveryCodesRemaining { get; set; }
}

/// <summary>Kết quả setup: FE hiển thị mã QR từ OtpAuthUri hoặc cho nhập tay Secret. Chỉ trả khi setup.</summary>
public class TwoFactorSetupResponse
{
    /// <summary>Bí mật dạng Base32 (nhập tay vào ứng dụng xác thực).</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>URI otpauth://totp/... để tạo mã QR.</summary>
    public string OtpAuthUri { get; set; } = string.Empty;
}

/// <summary>Mã khôi phục dùng một lần; chỉ hiển thị đúng một lần khi bật.</summary>
public class TwoFactorEnableResponse
{
    public List<string> RecoveryCodes { get; set; } = new List<string>();
}

/// <summary>
/// Trả thay cho AuthSessionResponse khi Admin đã bật xác thực hai bước. Chưa cấp phiên và chưa đặt cookie.
/// </summary>
public class TwoFactorChallengeResponse
{
    public bool TwoFactorRequired { get; set; } = true;

    public string ChallengeToken { get; set; } = string.Empty;

    /// <summary>Số giây còn lại trước khi thử thách hết hạn.</summary>
    public int ExpiresIn { get; set; }
}
