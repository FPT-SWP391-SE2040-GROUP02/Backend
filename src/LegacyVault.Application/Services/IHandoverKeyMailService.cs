using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Người thực hiện gửi khóa mở gói cho người nhận của một lượt qua email đã lưu của người nhận.</summary>
public interface IHandoverKeyMailService
{
    /// <summary>
    /// Kiểm khóa Người thực hiện nhập có khớp khóa đang hiệu lực của kho, rồi gửi khóa tới email người nhận. Khóa không được lưu.
    /// Lỗi: 401; 404 DEATH_CASE_NOT_FOUND (không phải Executor đang hiệu lực) hoặc HANDOVER_NOT_FOUND (lượt lạ);
    /// 403 HANDOVER_KEY_INVALID (khóa sai); 409 HANDOVER_NOT_ALLOWED reason PLAN_NOT_IN_HANDOVER / WRONG_STATE / NO_KEY / NO_EMAIL;
    /// 503 HANDOVER_NOT_ALLOWED reason KEY_SEND_FAILED (gửi lỗi, thử lại được).
    /// </summary>
    Task<SendHandoverKeyResponse> SendAsync(Guid caseId, Guid turnId, string? key, CancellationToken ct);
}
