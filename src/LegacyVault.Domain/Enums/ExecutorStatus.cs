namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái của một Executor trong kho.
/// </summary>
/// <remarks>
/// Chỉ Accepted mới được tính là Executor đang hiệu lực.
/// Khi kế hoạch quay về nháp, Executor đã chấp nhận chuyển sang Suspended và phải chấp nhận lại.
/// </remarks>
public enum ExecutorStatus
{
    /// <summary>Đã gửi lời mời, đang chờ người được mời trả lời (hết hạn sau 7 ngày).</summary>
    Invited = 0,
    /// <summary>Người được mời đã đăng nhập và bấm chấp nhận.</summary>
    Accepted = 1,
    /// <summary>Người được mời từ chối.</summary>
    Declined = 2,
    /// <summary>Quá 7 ngày không trả lời; Owner có thể mời lại.</summary>
    Expired = 3,
    /// <summary>Owner thu hồi lời mời hoặc thu hồi Executor.</summary>
    Revoked = 4,
    /// <summary>Kế hoạch quay về nháp; muốn kích hoạt lại thì Executor phải chấp nhận lại.</summary>
    Suspended = 5,
}
