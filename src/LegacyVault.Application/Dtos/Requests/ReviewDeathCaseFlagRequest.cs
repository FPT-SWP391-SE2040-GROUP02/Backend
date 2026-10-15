using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/verifier/death-cases/{caseId}/flags/{flagId}/review: Verifier xem xét một cờ (DEATH-10).
/// </summary>
public class ReviewDeathCaseFlagRequest
{
    /// <summary>Giá trị Etag lấy từ VerifierDeathCaseResponse lúc đọc hồ sơ (bắt buộc cho mọi thao tác ghi).</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang xét.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    /// <summary>Ghi chú xem xét, bắt buộc, tối đa 1000 ký tự (service kiểm và trả 400 theo trường note).</summary>
    public string? Note { get; set; }
}
