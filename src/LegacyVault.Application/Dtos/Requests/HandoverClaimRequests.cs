using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Một tệp người dùng tải lên, đã đọc vào bộ nhớ ở controller.</summary>
public class UploadedFile
{
    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>Body của POST /verifier/handover-records/{id}/claims/{claimId}/review.</summary>
public class ReviewHandoverClaimRequest
{
    /// <summary>ACCEPT, REQUEST_SUPPLEMENT hoặc REJECT.</summary>
    [Required(ErrorMessage = "Thiếu kết quả xét.")]
    public string Decision { get; set; } = string.Empty;

    /// <summary>Bắt buộc khi yêu cầu bổ sung hoặc từ chối; tối đa 1000 ký tự.</summary>
    [MaxLength(1000, ErrorMessage = "Lý do tối đa 1000 ký tự.")]
    public string? Reason { get; set; }
}

/// <summary>Body của POST /verifier/handover-reports/{id}/review.</summary>
public class ReviewHandoverReportRequest : ReviewHandoverClaimRequest
{
    /// <summary>
    /// Bắt buộc true (kèm lý do) khi chấp nhận hồ sơ có cảnh báo: họ tên không khớp, hoặc Chủ kho còn hoạt động sau khi hồ sơ được nộp.
    /// </summary>
    public bool ConfirmDocuments { get; set; }
}

/// <summary>Body của POST /verifier/handover-records/{id}/cancel.</summary>
public class CancelHandoverRecordRequest
{
    [Required(ErrorMessage = "Vui lòng ghi lý do hủy.")]
    [MaxLength(1000, ErrorMessage = "Lý do tối đa 1000 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Body của POST /verifier/handover-records/{id}/recipients/batch: thêm nhiều người nhận trong một lần lưu; một người
/// không hợp lệ thì không ai được thêm.
/// </summary>
public class AddHandoverRecipientsRequest
{
    [Required(ErrorMessage = "Thiếu danh sách người nhận.")]
    [MinLength(1, ErrorMessage = "Danh sách người nhận đang trống.")]
    [MaxLength(20, ErrorMessage = "Mỗi lần thêm tối đa 20 người nhận.")]
    public List<AddHandoverRecipientRequest> Recipients { get; set; } = new List<AddHandoverRecipientRequest>();
}

/// <summary>Body của POST /verifier/handover-records/{id}/recipients: luật sư thêm người nhận mới vào kho.</summary>
public class AddHandoverRecipientRequest
{
    [Required(ErrorMessage = "Thiếu họ tên.")]
    [MaxLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Thiếu số căn cước.")]
    public string CitizenId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Thiếu ngày sinh.")]
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Bắt buộc: thư mời nhận tài sản gửi tới email này.</summary>
    [Required(ErrorMessage = "Thiếu email.")]
    [MaxLength(256, ErrorMessage = "Email tối đa 256 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [MaxLength(32, ErrorMessage = "Số điện thoại tối đa 32 ký tự.")]
    public string? Phone { get; set; }
}
