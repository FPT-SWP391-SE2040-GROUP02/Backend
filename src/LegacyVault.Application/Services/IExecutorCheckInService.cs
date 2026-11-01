using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Executor điểm danh thay Owner khi Owner quá hạn và kho chưa có hồ sơ chứng tử đang mở (DMS-05).
/// </summary>
/// <remarks>
/// Người gọi không phải Executor đang hiệu lực của kho, hoặc là Owner → 404 VAULT_NOT_FOUND (SEC-04).
/// Lỗi: 409 CHECKIN_NOT_ALLOWED với reason DEATH_CASE_OPEN (kho đã có hồ sơ chứng tử đang mở)
/// hoặc PLAN_NOT_OVERDUE (Owner không còn quá hạn); 409 EDIT_CONFLICT (hai yêu cầu điểm danh song song).
/// </remarks>
public interface IExecutorCheckInService
{
    /// <summary>Đóng kỳ hiện tại, mở kỳ mới, đưa kế hoạch về ACTIVE — một giao dịch.</summary>
    Task<ExecutorCheckInResponse> CheckInForOwnerAsync(Guid vaultId, CancellationToken ct);
}
