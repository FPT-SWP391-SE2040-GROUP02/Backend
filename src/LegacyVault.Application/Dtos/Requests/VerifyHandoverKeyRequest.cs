using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Mã nhận do Người thực thi đưa trực tiếp cho người nhận.</summary>
public class VerifyHandoverKeyRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã nhận.")]
    [MaxLength(80, ErrorMessage = "Mã nhận không hợp lệ.")]
    public string Key { get; set; } = string.Empty;
}
