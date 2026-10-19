using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/auth/2fa/verify: hoàn tất đăng nhập Admin bằng mã TOTP hoặc mã khôi phục.</summary>
public class TwoFactorVerifyRequest
{
    [Required(ErrorMessage = "Thiếu mã thử thách.")]
    [MaxLength(256)]
    public string ChallengeToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cần nhập mã xác thực.")]
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Body của POST /api/v1/auth/2fa/enable và /disable: một mã TOTP (hoặc mã khôi phục khi tắt).</summary>
public class TwoFactorCodeRequest
{
    [Required(ErrorMessage = "Cần nhập mã xác thực.")]
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;
}
