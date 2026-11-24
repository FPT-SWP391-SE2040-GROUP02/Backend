using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class VaultMappings
{
    /// <summary>
    /// Cần vault đã nạp Plan và Entitlement; usedBytes/assignedAssetCount/usedBeneficiarySlots do service truy vấn riêng.
    /// </summary>
    public static VaultResponse ToResponse(this Vault vault, long usedBytes, int assignedAssetCount, int usedBeneficiarySlots)
    {
        var entitlement = vault.Entitlement;
        return new VaultResponse
        {
            Id = vault.Id,
            Name = vault.Name,
            Description = vault.Description,
            ExecutorNote = vault.ExecutorNote,
            ExecutorNoteUpdatedAt = vault.ExecutorNoteUpdatedAt,
            Status = vault.Status.ToApiString(),
            ClosedAt = vault.ClosedAt,
            PlanStatus = vault.Plan.Status.ToApiString(),
            PlanConfigVersion = vault.Plan.ConfigVersion,
            PlanActivatedAt = vault.Plan.ActivatedAt,
            ServiceTier = PlanCodes.Resolve(entitlement.Tier, entitlement.PlanCode),
            ServiceStatus = entitlement.Status.ToApiString(),
            ServiceExpiresAt = entitlement.ServiceExpiresAt,
            StorageQuotaBytes = EntitlementLimits.EffectiveQuotaBytes(entitlement),
            StorageUsedBytes = usedBytes,
            StorageRemainingBytes = Math.Max(0, EntitlementLimits.EffectiveQuotaBytes(entitlement) - usedBytes),
            BeneficiarySlots = EntitlementLimits.EffectiveIncludedSlots(entitlement),
            UsedBeneficiarySlots = usedBeneficiarySlots,
            AssignedAssetCount = assignedAssetCount,
            CreatedAt = vault.CreatedAt,
            UpdatedAt = vault.UpdatedAt,
            Etag = Etag.From(vault.UpdatedAt),
        };
    }
}
