using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class VaultMappings
{
    /// <summary>Cần vault đã nạp Plan và Entitlement; usedBytes/activeContainerCount do service truy vấn riêng.</summary>
    public static VaultResponse ToResponse(this Vault vault, long usedBytes, int activeContainerCount)
    {
        var entitlement = vault.Entitlement;
        return new VaultResponse
        {
            Id = vault.Id,
            Name = vault.Name,
            Description = vault.Description,
            Status = vault.Status.ToApiString(),
            ClosedAt = vault.ClosedAt,
            PlanStatus = vault.Plan.Status.ToApiString(),
            PlanConfigVersion = vault.Plan.ConfigVersion,
            PlanActivatedAt = vault.Plan.ActivatedAt,
            ServiceTier = entitlement.Tier.ToApiString(),
            ServiceStatus = entitlement.Status.ToApiString(),
            ServiceExpiresAt = entitlement.ServiceExpiresAt,
            StorageQuotaBytes = entitlement.StorageQuotaBytes,
            StorageUsedBytes = usedBytes,
            StorageRemainingBytes = Math.Max(0, entitlement.StorageQuotaBytes - usedBytes),
            BeneficiarySlots = entitlement.IncludedBeneficiarySlots + entitlement.ExtraBeneficiarySlots,
            ActiveContainerCount = activeContainerCount,
            CreatedAt = vault.CreatedAt,
            UpdatedAt = vault.UpdatedAt,
            Etag = Etag.From(vault.UpdatedAt),
        };
    }
}
