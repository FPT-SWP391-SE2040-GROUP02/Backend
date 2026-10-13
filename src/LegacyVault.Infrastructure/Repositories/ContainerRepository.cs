using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Chỉ là truy cập dữ liệu theo IContainerRepository. Luật nghiệp vụ gói (ContainerService) do module gói cài đặt.
/// VaultService dùng CountActiveByVaultIdAsync cho VaultResponse.ActiveContainerCount.
/// </remarks>
public sealed class ContainerRepository(AppDbContext context) : IContainerRepository
{
    public Task<Container?> FindByIdAsync(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        return context.Containers
            .Include(c => c.Assets)
            .FirstOrDefaultAsync(c => c.Id == containerId && c.VaultId == vaultId, ct);
    }

    public async Task<(List<Container> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, bool includeArchived, int pageIndex, int pageSize, string? searchTerm,
        string sortBy, bool isAscending, CancellationToken ct)
    {
        var query = context.Containers.AsNoTracking().Where(c => c.VaultId == vaultId);
        if (!includeArchived)
        {
            query = query.Where(c => c.Status != ContainerStatus.Archived);
        }
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(c => c.Name.Contains(searchTerm));
        }

        var totalCount = await query.CountAsync(ct);

        query = (sortBy, isAscending) switch
        {
            ("name", true) => query.OrderBy(c => c.Name).ThenBy(c => c.Id),
            ("name", false) => query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id),
            (_, true) => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            (_, false) => query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id),
        };

        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Include(c => c.Assets)
            .AsSplitQuery()
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<int> CountActiveByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Containers.CountAsync(
            c => c.VaultId == vaultId && c.Status != ContainerStatus.Archived, ct);
    }

    public Task<int> CountReadyByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Containers.CountAsync(
            c => c.VaultId == vaultId && c.Status == ContainerStatus.Ready, ct);
    }

    public Task<ContainerAsset?> FindLinkByAssetIdAsync(Guid assetId, CancellationToken ct)
    {
        return context.ContainerAssets.FirstOrDefaultAsync(l => l.AssetId == assetId, ct);
    }

    public void Add(Container container)
    {
        context.Containers.Add(container);
    }

    public async Task<bool> TryAddAssetLinkAsync(ContainerAsset link, CancellationToken ct)
    {
        context.ContainerAssets.Add(link);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            DbExceptions.DetachAll(context, link);
            return false;
        }
    }

    public void RemoveAssetLink(ContainerAsset link)
    {
        context.ContainerAssets.Remove(link);
    }
}
