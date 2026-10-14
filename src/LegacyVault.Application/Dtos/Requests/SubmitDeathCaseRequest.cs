using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/executor/death-cases/{caseId}/submit: Executor xác nhận bản hiện tại là đúng và gửi đi (DEATH-04).
/// </summary>
public class SubmitDeathCaseRequest
{
    /// <summary>Giá trị Etag lấy từ DeathCaseResponse lúc FE đọc hồ sơ.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang gửi.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    /// <summary>Executor xác nhận thông tin trong bản này là đúng. Phải là true.</summary>
    public bool Confirm { get; set; } = false;
}
