using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Executor xác nhận đúng người bằng tay khi người nhận không qua được eKYC (BR-122).</summary>
public interface IExecutorIdentityConfirmationService
{
    /// <summary>
    /// Xác nhận người đã nhận lượt đúng là người được chỉ định; lượt sang READY_TO_ACCEPT và hạn quyết định 7 ngày bắt đầu chạy.
    /// Bấm lặp khi Executor này đã xác nhận rồi thì trả lại đúng kết quả cũ, không đặt lại hạn.
    /// Lỗi: 401; 404 DEATH_CASE_NOT_FOUND (không phải Executor đang hiệu lực, Owner gọi) hoặc HANDOVER_NOT_FOUND (lượt lạ);
    /// 409 HANDOVER_NOT_ALLOWED reason PLAN_NOT_IN_HANDOVER / HOLD_ACTIVE / WRONG_STATE / OWN_TURN
    /// (lượt do chính tài khoản này nhận, không tự xác nhận được).
    /// </summary>
    Task<ConfirmHandoverIdentityResponse> ConfirmAsync(Guid caseId, Guid turnId, CancellationToken ct);
}
