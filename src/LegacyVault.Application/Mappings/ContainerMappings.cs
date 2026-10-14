using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class ContainerMappings
{
    /// <summary>Cần container đã nạp Assets; recipientCount do service truy vấn riêng (chưa có module người nhận → 0).</summary>
    public static ContainerResponse ToResponse(this Container container, int recipientCount)
    {
        List<Guid> assetIds = new List<Guid>();
        foreach (ContainerAsset link in container.Assets)
        {
            assetIds.Add(link.AssetId);
        }

        return new ContainerResponse
        {
            Id = container.Id,
            VaultId = container.VaultId,
            Name = container.Name,
            Description = container.Description,
            Status = container.Status.ToApiString(),
            AssetIds = assetIds,
            MissingRequirements = ContainerRules.GetMissingRequirements(container, recipientCount),
            CreatedAt = container.CreatedAt,
            UpdatedAt = container.UpdatedAt,
            ArchivedAt = container.ArchivedAt,
            Etag = Etag.From(container.UpdatedAt),
        };
    }
}
