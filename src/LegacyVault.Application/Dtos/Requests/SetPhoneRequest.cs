using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Số điện thoại của người đang đăng nhập (bắt buộc với tài khoản tạo bằng Google chưa có số).</summary>
public class SetPhoneRequest
{
    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [MaxLength(20, ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string Phone { get; set; } = string.Empty;
}
