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
