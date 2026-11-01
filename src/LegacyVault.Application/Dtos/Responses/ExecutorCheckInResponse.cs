namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả Executor điểm danh thay chủ kho (POST /api/v1/executor/vaults/{vaultId}/check-in).</summary>
public class ExecutorCheckInResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Trạng thái kế hoạch sau khi điểm danh; luôn là ACTIVE.</summary>
    public string PlanStatus { get; set; } = string.Empty;

    public DateTimeOffset CheckedInAt { get; set; }

    /// <summary>Hạn điểm danh của kỳ mới vừa mở.</summary>
    public DateTimeOffset NextCheckInDueAt { get; set; }

    public DateTimeOffset GraceEndsAt { get; set; }
}
