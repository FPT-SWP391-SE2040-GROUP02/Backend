namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền gửi lại lời mời F3 chưa giao được: lần gửi đầu lỗi, hoặc tiến trình dừng sau khi lưu lượt
/// nhưng trước khi gửi thư. Mỗi hàm Process* đọc lại dữ liệu và kiểm lại điều kiện nên chạy lặp là an toàn.
/// </summary>
public interface IHandoverInvitationRetryJob
{
    /// <summary>Id các lượt cần thử gửi (đã đủ tuổi, chưa đủ số lần thử). Còn phải kiểm giãn cách ở ProcessTurnAsync.</summary>
    Task<IReadOnlyList<Guid>> FindPendingTurnIdsAsync(CancellationToken ct);

    /// <summary>Thử gửi một lượt. Thành công thì đặt InvitationSentAt; đủ số lần lỗi thì ghi log cảnh báo và ngừng thử.</summary>
    Task ProcessTurnAsync(Guid turnId, CancellationToken ct);
}
