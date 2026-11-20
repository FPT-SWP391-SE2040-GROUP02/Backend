namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một kho mà người dùng đang là Executor (GET /api/v1/executor/vaults).</summary>
public class ExecutorVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên Owner theo tài khoản, để Executor nhận ra kho.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Hướng dẫn Owner để lại cho Executor, xem được trước khi hồ sơ chứng tử được duyệt; null khi Owner chưa viết.</summary>
    public string? ExecutorNote { get; set; }

    public DateTimeOffset? ExecutorNoteUpdatedAt { get; set; }

    /// <summary>Hồ sơ chứng tử đang mở của kho (kể cả bản nháp); null khi chưa có.</summary>
    public Guid? OpenDeathCaseId { get; set; }

    /// <summary>Hồ sơ bị từ chối gần nhất của kho; Executor mở bằng GET hồ sơ để xem lý do. Null khi chưa có.</summary>
    public Guid? LastRejectedDeathCaseId { get; set; }

    /// <summary>Lúc tài khoản chấp nhận lời mời làm Người thực thi của kho; null khi dữ liệu cũ không có. FE hiện "Bạn nhận vai ngày ...".</summary>
    public DateTimeOffset? AcceptedAt { get; set; }
}
