using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Khóa mở gói Người thực hiện đang giữ, để Backend kiểm rồi gửi cho người nhận.</summary>
public class SendHandoverKeyRequest
{
    [Required(ErrorMessage = "Vui lòng nhập khóa mở gói.")]
    [MaxLength(80, ErrorMessage = "Khóa mở gói không hợp lệ.")]
    public string Key { get; set; } = string.Empty;
}
