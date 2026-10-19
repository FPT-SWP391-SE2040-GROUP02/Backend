using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Executor gửi lại lời mời nhận tài sản cho lượt chưa ai nhận (INVITE-02, INVITE-04).</summary>
public interface IHandoverInvitationService
{
    /// <summary>
    /// Gửi lại lời mời của một lượt qua email đã lưu của người nhận. Link cũ bị vô hiệu vì DB chỉ giữ mã băm (INVITE-01).
    /// Lỗi: 401; 404 DEATH_CASE_NOT_FOUND (không phải Executor đang hiệu lực, Owner gọi) hoặc HANDOVER_NOT_FOUND (lượt lạ);
    /// 409 HANDOVER_NOT_ALLOWED reason PLAN_NOT_IN_HANDOVER / HOLD_ACTIVE / WRONG_STATE / NO_EMAIL / RESEND_TOO_SOON;
    /// 503 HANDOVER_NOT_ALLOWED reason INVITATION_SEND_FAILED (gửi lỗi, không đổi gì, thử lại được).
    /// </summary>
    Task<ResendHandoverInvitationResponse> ResendAsync(Guid caseId, Guid turnId, CancellationToken ct);
}
