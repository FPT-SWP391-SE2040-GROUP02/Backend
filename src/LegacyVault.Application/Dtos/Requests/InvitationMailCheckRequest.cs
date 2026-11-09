using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/handover/invitations/{token}/check-mail.</summary>
public class InvitationMailCheckRequest
{
    /// <summary>Mã hồ sơ ghi trong thư (8 ký tự).</summary>
    [Required(ErrorMessage = "Vui lòng nhập mã hồ sơ.")]
    [MaxLength(16)]
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>4 số cuối căn cước của người nhận.</summary>
    [Required(ErrorMessage = "Vui lòng nhập 4 số cuối căn cước.")]
    [RegularExpression("^[0-9]{4}$", ErrorMessage = "Nhập đúng 4 chữ số.")]
    public string CitizenIdLast4 { get; set; } = string.Empty;
}
