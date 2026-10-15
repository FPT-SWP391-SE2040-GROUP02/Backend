using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/verifier/death-cases/{caseId}/request-supplement: yêu cầu Executor bổ sung hồ sơ (DEATH-01).
/// </summary>
public class RequestDeathCaseSupplementRequest
{
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang xét.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    /// <summary>Lý do, bắt buộc, tối đa 1000 ký tự (service kiểm và trả 400 theo trường reason).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Các phần cần bổ sung, bắt buộc có ít nhất một. Mã viết hoa đúng như missingRequirements:
    /// OWNER_FULL_NAME, OWNER_CITIZEN_ID, DATE_OF_DEATH, CERTIFICATE_NUMBER, ISSUED_ON, ISSUED_BY, FILES.
    /// </summary>
    public List<string>? RequestedParts { get; set; }
}
