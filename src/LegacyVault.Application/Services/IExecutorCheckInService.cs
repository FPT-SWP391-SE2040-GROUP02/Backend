using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Executor điểm danh thay chủ kho khi chủ kho đã quá hạn điểm danh, dùng trong thời gian chờ có giấy chứng tử.
/// </summary>
/// <remarks>
/// Điểm danh hộ chỉ gia hạn lịch: đóng kỳ đang quá hạn, mở kỳ mới và đưa kế hoạch về ACTIVE.
/// Nó không được tính là chủ kho đã điểm danh: kỳ cũ đóng mà không ghi giờ điểm danh, và giờ "điểm danh gần nhất"
/// của chủ kho giữ nguyên. Nhờ vậy, khi Executor gửi hồ sơ chứng tử, các lượt điểm danh hộ không làm bật cờ
/// "có điểm danh sau ngày mất".
/// </remarks>
public interface IExecutorCheckInService
{
    /// <summary>
    /// Điểm danh thay chủ kho của kho vaultId.
    /// Chỉ Executor đang hiệu lực (đã chấp nhận, không phải chủ kho) được gọi; người khác nhận 404 như kho không tồn tại.
    /// Lỗi: 404 VAULT_NOT_FOUND;
    /// 409 CHECKIN_NOT_ALLOWED kèm planStatus và reason = PLAN_NOT_OVERDUE (kế hoạch không ở CHECKIN_OVERDUE
    /// hoặc AWAITING_CHECK) hoặc DEATH_CASE_OPEN (kho đã có hồ sơ chứng tử đang mở);
    /// 409 EDIT_CONFLICT khi có người khác điểm danh cùng lúc.
    /// </summary>
    Task<ExecutorCheckInResponse> CheckInForOwnerAsync(Guid vaultId, CancellationToken ct);
}
