using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/handover/invitations/{token}/check-mail.</summary>
public class InvitationMailCheckRequest
{
    /// <summary>Mã hồ sơ ghi trong thư (8 ký tự).</summary>
    [Required(ErrorMessage = "Vui lòng nhập mã hồ sơ.")]
    [MaxLength(16)]
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>Số căn cước đầy đủ của người nhận.</summary>
    [Required(ErrorMessage = "Vui lòng nhập số căn cước.")]
    [RegularExpression("^[0-9]{12}$", ErrorMessage = "Số căn cước gồm 12 chữ số.")]
    public string CitizenId { get; set; } = string.Empty;
}
