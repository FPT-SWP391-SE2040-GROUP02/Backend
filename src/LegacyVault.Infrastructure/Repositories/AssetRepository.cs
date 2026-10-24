using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class AssetRepository(AppDbContext context) : IAssetRepository
{
    public Task<bool> IsPinnedInHandoverAsync(Guid assetId, CancellationToken ct)
    {
        return context.Set<HandoverSnapshotItem>().AnyAsync(i => i.AssetId == assetId, ct);
    }

    public void Remove(Asset asset)
    {
        context.Assets.Remove(asset);
    }

    public Task<Asset?> FindByIdAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        return context.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.VaultId == vaultId, ct);
    }

    // ASSET-03, AC-68: tài sản trong gói DRAFT cũng đã có liên kết nên không liệt kê lại ở đây.
    public Task<List<Asset>> FindOutsideContainerByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Assets
            .AsNoTracking()
            .Where(a => a.VaultId == vaultId && !context.ContainerAssets.Any(l => l.AssetId == a.Id))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);
    }

    public async Task<(List<Asset> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, int pageIndex, int pageSize, string? searchTerm,
        string sortBy, bool isAscending, CancellationToken ct)
    {
        var query = context.Assets.AsNoTracking().Where(a => a.VaultId == vaultId);
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(a => a.Name.Contains(searchTerm));
        }

        var totalCount = await query.CountAsync(ct);

        // Thêm ThenBy(Id) để thứ tự ổn định khi nhiều tài sản trùng tên/thời gian.
        if (sortBy == "name")
        {
            if (isAscending)
            {
                query = query.OrderBy(a => a.Name).ThenBy(a => a.Id);
            }
            else
            {
                query = query.OrderByDescending(a => a.Name).ThenByDescending(a => a.Id);
            }
        }
        else
        {
            if (isAscending)
            {
                query = query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id);
            }
            else
            {
                query = query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id);
            }
        }

        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public void Add(Asset asset)
    {
        context.Assets.Add(asset);
    }
}
