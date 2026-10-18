namespace LegacyVault.Application.Dtos.Responses;

/// <summary>ASSET-03, AC-68: tài sản chưa thuộc gói nào, chưa nằm trong phạm vi bàn giao.</summary>
public class PlanSetupAssetResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}
