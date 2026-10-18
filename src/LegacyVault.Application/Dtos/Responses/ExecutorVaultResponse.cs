namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một kho mà người dùng đang là Executor (GET /api/v1/executor/vaults, DMS-05).</summary>
public class ExecutorVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên Owner theo tài khoản, để Executor nhận ra kho.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>PRIMARY | BACKUP.</summary>
    public string Role { get; set; } = string.Empty;

    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>DMS-05: Owner đã quá hạn điểm danh (kế hoạch CHECKIN_OVERDUE hoặc AWAITING_CHECK).</summary>
    public bool CheckInOverdue { get; set; }

    /// <summary>Hồ sơ chứng tử đang mở của kho (kể cả bản nháp); null khi chưa có.</summary>
    public Guid? OpenDeathCaseId { get; set; }
}
