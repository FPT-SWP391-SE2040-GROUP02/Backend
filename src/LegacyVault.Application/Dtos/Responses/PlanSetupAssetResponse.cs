namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Tài sản chưa gán cho người nhận nào, chưa nằm trong phạm vi bàn giao.</summary>
public class PlanSetupAssetResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}
