using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/ekyc/consent: người dùng đồng ý cho chụp giấy tờ và khuôn mặt (EKYC-03).
/// </summary>
public class EkycConsentRequest
{
    /// <summary>OWNER_ENROLLMENT, BENEFICIARY_CLAIM hoặc OWNER_ALIVE (service kiểm và trả 400 theo trường purpose).</summary>
    [Required(ErrorMessage = "Vui lòng chọn mục đích xác minh.")]
    [MaxLength(32)]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Phiên bản thông báo mà người dùng đã thấy; phải bằng phiên bản hiện hành của server.</summary>
    [Required(ErrorMessage = "Thiếu phiên bản thông báo.")]
    [MaxLength(32)]
    public string NoticeVersion { get; set; } = string.Empty;
}
