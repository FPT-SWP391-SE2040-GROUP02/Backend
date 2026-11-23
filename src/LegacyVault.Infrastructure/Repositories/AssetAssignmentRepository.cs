using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Chỉ là truy cập dữ liệu theo IAssetAssignmentRepository. Luật gán tài sản nằm ở AssetAssignmentService.
/// </remarks>
public sealed class AssetAssignmentRepository(AppDbContext context) : IAssetAssignmentRepository
{
    public Task<List<AssetAssignment>> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.AssetAssignments
            .Where(a => a.Asset.VaultId == vaultId)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);
    }

    public Task<List<AssetAssignment>> FindDetailedByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.AssetAssignments
            .AsNoTracking()
            .Include(a => a.Asset)
            .Include(a => a.Beneficiary)
            .Where(a => a.Asset.VaultId == vaultId)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);
    }

    public Task<int> CountByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.AssetAssignments.CountAsync(a => a.Asset.VaultId == vaultId, ct);
    }

    public Task<int> CountAssignedAssetsAsync(Guid vaultId, CancellationToken ct)
    {
        return context.AssetAssignments
            .Where(a => a.Asset.VaultId == vaultId)
            .Select(a => a.AssetId)
            .Distinct()
            .CountAsync(ct);
    }

    public Task<int> CountUsedBeneficiarySlotsAsync(Guid vaultId, CancellationToken ct)
    {
        return context.AssetAssignments
            .Where(a => a.Asset.VaultId == vaultId)
            .Select(a => a.BeneficiaryId)
            .Distinct()
            .CountAsync(ct);
    }

    public Task<bool> IsAssetAssignedAsync(Guid assetId, CancellationToken ct)
    {
        return context.AssetAssignments.AnyAsync(a => a.AssetId == assetId, ct);
    }

    public async Task<Dictionary<Guid, List<Guid>>> FindAssetIdsByBeneficiaryIdsAsync(
        List<Guid> beneficiaryIds, CancellationToken ct)
    {
        Dictionary<Guid, List<Guid>> result = new Dictionary<Guid, List<Guid>>();
        if (beneficiaryIds.Count == 0)
        {
            return result;
        }

        var rows = await context.AssetAssignments
            .AsNoTracking()
            .Where(a => beneficiaryIds.Contains(a.BeneficiaryId))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => new { a.BeneficiaryId, a.AssetId })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            List<Guid>? assetIds;
            if (!result.TryGetValue(row.BeneficiaryId, out assetIds))
            {
                assetIds = new List<Guid>();
                result[row.BeneficiaryId] = assetIds;
            }
            assetIds.Add(row.AssetId);
        }
        return result;
    }

    public void Add(AssetAssignment assignment)
    {
        context.AssetAssignments.Add(assignment);
    }

    public void Remove(AssetAssignment assignment)
    {
        context.AssetAssignments.Remove(assignment);
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            // Bỏ hết thay đổi chưa lưu (gồm cả lần tăng phiên bản cấu hình kế hoạch) để không ghi nửa chừng.
            context.ChangeTracker.Clear();
            return false;
        }
    }
}
