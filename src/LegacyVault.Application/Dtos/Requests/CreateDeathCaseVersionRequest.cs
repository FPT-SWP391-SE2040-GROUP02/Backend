using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/executor/death-cases/{caseId}/versions: tạo phiên bản bổ sung từ bản đã gửi (DEATH-04, D5).
/// </summary>
public class CreateDeathCaseVersionRequest
{
    /// <summary>Giá trị Etag lấy từ DeathCaseResponse lúc FE đọc hồ sơ.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang sửa.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;
}
