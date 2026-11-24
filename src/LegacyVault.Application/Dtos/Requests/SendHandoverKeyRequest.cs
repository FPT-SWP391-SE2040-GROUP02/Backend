using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Mã nhận Người thực thi đang giữ, để Backend kiểm rồi gửi cho người nhận.</summary>
public class SendHandoverKeyRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã nhận.")]
    [MaxLength(80, ErrorMessage = "Mã nhận không hợp lệ.")]
    public string Key { get; set; } = string.Empty;
}
