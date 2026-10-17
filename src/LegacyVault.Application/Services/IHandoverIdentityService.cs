using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Nối kết quả eKYC của người nhận với lượt bàn giao: eKYC đạt và khớp người được chỉ định thì lượt từ
/// "đang xác minh danh tính" sang "sẵn sàng nhận" để người nhận được đồng ý hoặc từ chối (SRS §7.2).
/// </summary>
/// <remarks>
/// - Lượt của người khác hoặc id lạ đều trả cùng một 404 HANDOVER_NOT_FOUND (SEC-04).
/// - eKYC chỉ cung cấp bằng chứng; việc đổi trạng thái lượt do service này làm, không phải module eKYC.
/// - Chưa có bước gọi video với Executor (IDENTITY-01): khi có thì bước đó phải nằm giữa eKYC và "sẵn sàng nhận".
/// </remarks>
public interface IHandoverIdentityService
{
    /// <summary>
    /// Chốt bước xác minh danh tính: kiểm lượt eKYC mới nhất ghim đúng lượt này rồi đưa lượt sang READY_TO_ACCEPT
    /// và đặt hạn quyết định 7 ngày. Gọi lặp khi lượt đã READY_TO_ACCEPT là an toàn, không đổi gì.
    /// Lỗi: 404 HANDOVER_NOT_FOUND; 409 HANDOVER_NOT_ALLOWED với reason PLAN_NOT_IN_HANDOVER, WRONG_STATE (lượt không
    /// ở bước xác minh), IDENTITY_NOT_VERIFIED (chưa làm eKYC cho lượt này), IDENTITY_NOT_FINISHED (chưa so khớp mặt),
    /// IDENTITY_FAILED, IDENTITY_NEEDS_REVIEW, IDENTITY_WRONG_PERSON.
    /// </summary>
    Task<HandoverTurnResponse> CompleteIdentityAsync(Guid turnId, CancellationToken ct);
}
