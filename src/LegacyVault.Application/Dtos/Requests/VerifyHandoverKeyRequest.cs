using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Khóa mở gói do Người thực hiện đưa trực tiếp cho người nhận.</summary>
public class VerifyHandoverKeyRequest
{
    [Required(ErrorMessage = "Vui lòng nhập khóa mở gói.")]
    [MaxLength(80, ErrorMessage = "Khóa mở gói không hợp lệ.")]
    public string Key { get; set; } = string.Empty;
}
