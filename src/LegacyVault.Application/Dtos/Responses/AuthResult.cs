namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Kết quả nội bộ giữa service và controller.
/// </summary>
/// <remarks>
/// Controller trả Session làm JSON (có access token) và ghi RefreshToken vào cookie HttpOnly.
/// Không bao giờ trả nguyên object này ra JSON: refresh token không được lọt vào body.
/// </remarks>
public class AuthResult
{
    public AuthSessionResponse Session { get; set; } = new AuthSessionResponse();

    /// <summary>Chỉ để ghi cookie HttpOnly.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    public DateTimeOffset RefreshTokenExpiresAt { get; set; }

    /// <summary>true = "Ghi nhớ đăng nhập" → cookie refresh sống qua khi tắt trình duyệt.</summary>
    public bool IsPersistent { get; set; }

    /// <summary>Khác null khi Admin đã bật xác thực hai bước: chưa có phiên/refresh token, chỉ có thử thách.</summary>
    public TwoFactorChallengeResponse? TwoFactor { get; set; }
}
