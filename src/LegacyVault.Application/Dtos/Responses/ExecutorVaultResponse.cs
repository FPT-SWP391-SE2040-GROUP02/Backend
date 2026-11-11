namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một kho mà người dùng đang là Executor (GET /api/v1/executor/vaults, DMS-05).</summary>
public class ExecutorVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên Owner theo tài khoản, để Executor nhận ra kho.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Hướng dẫn Owner để lại cho Executor, xem được trước khi hồ sơ chứng tử được duyệt; null khi Owner chưa viết.</summary>
    public string? ExecutorNote { get; set; }

    public DateTimeOffset? ExecutorNoteUpdatedAt { get; set; }

    /// <summary>DMS-05: Owner đã quá hạn điểm danh (kế hoạch CHECKIN_OVERDUE hoặc AWAITING_CHECK).</summary>
    public bool CheckInOverdue { get; set; }

    /// <summary>Hồ sơ chứng tử đang mở của kho (kể cả bản nháp); null khi chưa có.</summary>
    public Guid? OpenDeathCaseId { get; set; }

    /// <summary>Hồ sơ bị từ chối gần nhất của kho; Executor mở bằng GET hồ sơ để xem lý do. Null khi chưa có.</summary>
    public Guid? LastRejectedDeathCaseId { get; set; }

    /// <summary>
    /// Lần chủ kho tự điểm danh gần nhất. Điểm danh thay của Executor không tính; null khi chủ kho chưa từng tự điểm danh.
    /// FE dùng để báo xanh "chủ kho đã điểm danh thành công" khi kế hoạch đang an toàn.
    /// </summary>
    public DateTimeOffset? LastOwnerCheckInAt { get; set; }

    /// <summary>Lúc tài khoản chấp nhận lời mời làm Người thực thi của kho; null khi dữ liệu cũ không có. FE hiện "Bạn nhận vai ngày ...".</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Số lần điểm danh thay liên tiếp tối đa (Admin đặt, mặc định 3).</summary>
    public int ProxyCheckInLimit { get; set; }

    /// <summary>Số lần đã điểm danh thay liên tiếp; về 0 khi chủ kho tự điểm danh.</summary>
    public int ProxyCheckInsUsed { get; set; }

    /// <summary>Số lượt điểm danh thay còn lại (không âm).</summary>
    public int ProxyCheckInsLeft { get; set; }
}
