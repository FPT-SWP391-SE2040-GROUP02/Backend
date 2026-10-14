namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền của điểm danh: chuyển trạng thái kế hoạch khi quá hạn và gửi nhắc (SRS §6.1, DMS-02, DMS-06, TIME-05).
/// </summary>
/// <remarks>
/// Hai hàm tách nhau để CheckInMonitor mở một DI scope (một DbContext) riêng cho mỗi kỳ:
/// một kỳ lỗi không làm hỏng kỳ khác. Mọi trạng thái nằm trong DB nên chạy lặp hoặc chạy trên hai instance
/// không tạo trùng việc hay trùng nhắc.
/// </remarks>
public interface ICheckInJob
{
    /// <summary>Id các kỳ đang mở cần xem xét lúc này (đã tới hạn nhắc sớm nhất hoặc quá hạn).</summary>
    Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(CancellationToken ct);

    /// <summary>
    /// Xử lý một kỳ: chuyển ACTIVE → CHECKIN_OVERDUE → AWAITING_CHECK nếu đã tới mốc, rồi gửi nhắc mốc mới nhất.
    /// Gửi nhắc lỗi thì lời nhắc ở lại trạng thái chờ và exception được ném lại cho nơi gọi ghi log.
    /// RowVersion lệch (Owner vừa điểm danh) → ném AppException EDIT_CONFLICT; vòng sau đánh giá lại.
    /// </summary>
    Task ProcessPeriodAsync(Guid periodId, CancellationToken ct);
}
