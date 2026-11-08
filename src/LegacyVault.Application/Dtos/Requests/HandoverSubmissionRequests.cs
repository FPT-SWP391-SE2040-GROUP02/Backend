using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Thông tin khai trên văn bản mẫu khi người nhận nộp hồ sơ (kèm hai ảnh căn cước gửi riêng dạng tệp).</summary>
public class SubmitHandoverSubmissionRequest
{
    /// <summary>ACCEPT hoặc DECLINE.</summary>
    [Required(ErrorMessage = "Vui lòng chọn Chấp nhận hoặc Từ chối.")]
    [RegularExpression("^(ACCEPT|DECLINE)$", ErrorMessage = "Lựa chọn không hợp lệ.")]
    public string Kind { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số căn cước.")]
    [RegularExpression("^[0-9]{12}$", ErrorMessage = "Số căn cước gồm 12 chữ số.")]
    public string CitizenId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập ngày sinh.")]
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Phải true: đã đọc điều khoản của lựa chọn.</summary>
    public bool Acknowledged { get; set; }

    /// <summary>Phải true khi DECLINE: tự nguyện từ chối, không nhằm trốn tránh nghĩa vụ tài sản.</summary>
    public bool VoluntaryDecline { get; set; }
}

/// <summary>Body của POST /api/v1/verifier/handover-submissions/{id}/request-supplement.</summary>
public class RequestHandoverSupplementRequest
{
    [Required(ErrorMessage = "Vui lòng nhập lý do.")]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
