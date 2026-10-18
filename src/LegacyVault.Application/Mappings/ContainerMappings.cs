using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class ContainerMappings
{
    /// <summary>Cần container đã nạp Assets và Designations (repository đã sắp Designations theo thời điểm gắn).</summary>
    public static ContainerResponse ToResponse(this Container container)
    {
        List<Guid> assetIds = new List<Guid>();
        foreach (ContainerAsset link in container.Assets)
        {
            assetIds.Add(link.AssetId);
        }

        List<Guid> beneficiaryIds = new List<Guid>();
        foreach (Designation designation in container.Designations)
        {
            beneficiaryIds.Add(designation.BeneficiaryId);
        }

        return new ContainerResponse
        {
            Id = container.Id,
            VaultId = container.VaultId,
            Name = container.Name,
            Description = container.Description,
            Status = container.Status.ToApiString(),
            AssetIds = assetIds,
            BeneficiaryIds = beneficiaryIds,
            MissingRequirements = ContainerRules.GetMissingRequirements(container),
            CreatedAt = container.CreatedAt,
            UpdatedAt = container.UpdatedAt,
            ArchivedAt = container.ArchivedAt,
            Etag = Etag.From(container.UpdatedAt),
        };
    }
}
