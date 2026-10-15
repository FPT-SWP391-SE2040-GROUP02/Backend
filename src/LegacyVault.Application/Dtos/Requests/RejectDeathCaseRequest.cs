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
}
