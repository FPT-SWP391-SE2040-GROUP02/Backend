namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một kho trong ô "Chọn hồ sơ thử" của công cụ thử nghiệm. Không chứa nội dung kho.</summary>
public class DevVaultOptionResponse
{
    public Guid VaultId { get; set; }

    public string VaultName { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    /// <summary>DRAFT | ACTIVE | CHECKIN_OVERDUE | AWAITING_CHECK | DEATH_CASE_OPEN | HANDOVER_IN_PROGRESS | CLOSED | BLOCKED.</summary>
    public string PlanStatus { get; set; } = string.Empty;
}
