namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một kho mà người dùng đang là Người thực thi (GET /api/v1/executor/vaults).</summary>
public class ExecutorVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên Chủ kho theo tài khoản, để Người thực thi nhận ra kho.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Hướng dẫn Chủ kho để lại cho Người thực thi; null khi Chủ kho chưa viết.</summary>
    public string? ExecutorNote { get; set; }

    public DateTimeOffset? ExecutorNoteUpdatedAt { get; set; }

    /// <summary>Lúc tài khoản chấp nhận lời mời làm Người thực thi của kho; null khi dữ liệu cũ không có.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Biên bản bàn giao của kho; null khi chưa mở bàn giao.</summary>
    public Guid? HandoverRecordId { get; set; }

    /// <summary>DRAFTING, IDENTITY_CHECK, SIGNING, COMPLETED; rỗng khi chưa mở bàn giao.</summary>
    public string HandoverRecordStatus { get; set; } = string.Empty;
}
