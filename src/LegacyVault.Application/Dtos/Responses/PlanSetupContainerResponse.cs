namespace LegacyVault.Application.Dtos.Responses;

/// <summary>SETUP-01: gói READY hoặc DRAFT hiển thị trên màn kích hoạt kế hoạch.</summary>
public class PlanSetupContainerResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AssetCount { get; set; }
    public int DesignationCount { get; set; }
    public List<string> MissingRequirements { get; set; } = new List<string>();
}
