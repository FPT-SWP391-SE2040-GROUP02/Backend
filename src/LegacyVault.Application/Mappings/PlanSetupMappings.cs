using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class PlanSetupMappings
{
    /// <summary>Chỉ trả metadata tài sản chưa gán cho ai, không đọc nội dung hoặc phiên bản.</summary>
    public static List<PlanSetupAssetResponse> ToAssetItems(List<Asset> assets)
    {
        List<PlanSetupAssetResponse> items = new List<PlanSetupAssetResponse>();
        foreach (Asset asset in assets)
        {
            PlanSetupAssetResponse item = new PlanSetupAssetResponse
            {
                Id = asset.Id,
                Name = asset.Name,
                Kind = asset.Kind.ToApiString(),
            };
            items.Add(item);
        }

        return items;
    }
}
