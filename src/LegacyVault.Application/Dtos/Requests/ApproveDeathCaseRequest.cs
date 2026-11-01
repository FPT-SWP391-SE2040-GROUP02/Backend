using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/verifier/death-cases/{caseId}/approve: duyệt hồ sơ chứng tử và mở bàn giao F3 (DEATH-06).
/// </summary>
public class ApproveDeathCaseRequest
{
    /// <summary>Giá trị Etag lấy từ VerifierDeathCaseResponse (dùng etag mới nhất, kể cả sau khi xem xét cờ).</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang xét.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    /// <summary>Lý do / ghi chú duyệt, bắt buộc, tối đa 1000 ký tự (service kiểm và trả 400 theo trường reason).</summary>
    public string? Reason { get; set; }

    /// <summary>DEATH-03: mật khẩu tài khoản để xác nhận quyết định. Không log, không ghi nhật ký.</summary>
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu để xác nhận.")]
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;
}
