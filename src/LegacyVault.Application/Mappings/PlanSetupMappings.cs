using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class PlanSetupMappings
{
    /// <summary>SETUP-01: gói đã được repository nạp Assets và Designations, sắp theo thời điểm tạo.</summary>
    public static List<PlanSetupContainerResponse> ToContainerItems(List<Container> containers)
    {
        List<PlanSetupContainerResponse> items = new List<PlanSetupContainerResponse>();
        foreach (Container container in containers)
        {
            PlanSetupContainerResponse item = new PlanSetupContainerResponse
            {
                Id = container.Id,
                Name = container.Name,
                Status = container.Status.ToApiString(),
                AssetCount = container.Assets.Count,
                DesignationCount = container.Designations.Count,
                MissingRequirements = ContainerRules.GetMissingRequirements(container),
            };
            items.Add(item);
        }

        return items;
    }

    /// <summary>ASSET-03, AC-68: chỉ trả metadata tài sản ngoài gói, không đọc nội dung hoặc phiên bản.</summary>
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
