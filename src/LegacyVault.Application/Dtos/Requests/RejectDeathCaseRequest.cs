using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/verifier/death-cases/{caseId}/reject: từ chối hồ sơ chứng tử (DEATH-12).
/// </summary>
public class RejectDeathCaseRequest
{
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang xét.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    /// <summary>Lý do từ chối, bắt buộc, tối đa 1000 ký tự (service kiểm và trả 400 theo trường reason). Executor sẽ thấy.</summary>
    public string? Reason { get; set; }

    /// <summary>DEATH-14: mật khẩu tài khoản để xác nhận ký số quyết định. Không log, không ghi nhật ký.</summary>
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu để xác nhận ký.")]
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;
}
