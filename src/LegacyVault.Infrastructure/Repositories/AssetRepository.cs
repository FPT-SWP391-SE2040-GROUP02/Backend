using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class AssetRepository(AppDbContext context) : IAssetRepository
{
    public Task<Asset?> FindByIdAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        return context.Assets
            .Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.Id == assetId && a.VaultId == vaultId, ct);
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
            .Include(a => a.Versions)
            .AsSplitQuery()
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public void Add(Asset asset)
    {
        context.Assets.Add(asset);
    }
}
