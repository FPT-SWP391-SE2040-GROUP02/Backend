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
            .Include(c => c.Designations.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id))
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

        // ThenBy(Id): khi trùng tên/thời điểm tạo vẫn có thứ tự cố định, chuyển trang không lặp/sót.
        if (sortBy == "name")
        {
            if (isAscending)
            {
                query = query.OrderBy(c => c.Name).ThenBy(c => c.Id);
            }
            else
            {
                query = query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id);
            }
        }
        else
        {
            // Mặc định: sắp theo thời điểm tạo (service đã chặn các giá trị sortBy khác).
            if (isAscending)
            {
                query = query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);
            }
            else
            {
                query = query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id);
            }
        }

        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Include(c => c.Assets)
            .Include(c => c.Designations.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id))
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

    public async Task<bool> TryAddDesignationAsync(Designation designation, CancellationToken ct)
    {
        context.Designations.Add(designation);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            DbExceptions.DetachAll(context, designation);
            return false;
        }
    }

    public void RemoveDesignation(Designation designation)
    {
        context.Designations.Remove(designation);
    }

    public Task<int> CountUsedBeneficiarySlotsAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Designations
            .Where(d => d.Container.VaultId == vaultId)
            .Select(d => d.BeneficiaryId)
            .Distinct()
            .CountAsync(ct);
    }

    public Task<bool> IsBeneficiaryDesignatedAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        return context.Designations
            .AnyAsync(d => d.BeneficiaryId == beneficiaryId && d.Container.VaultId == vaultId, ct);
    }

    public async Task<Dictionary<Guid, List<Guid>>> FindContainerIdsByBeneficiaryIdsAsync(
        List<Guid> beneficiaryIds, CancellationToken ct)
    {
        Dictionary<Guid, List<Guid>> result = new Dictionary<Guid, List<Guid>>();
        if (beneficiaryIds.Count == 0)
        {
            return result;
        }

        var rows = await context.Designations
            .AsNoTracking()
            .Where(d => beneficiaryIds.Contains(d.BeneficiaryId))
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Select(d => new { d.BeneficiaryId, d.ContainerId })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            List<Guid>? containerIds;
            if (!result.TryGetValue(row.BeneficiaryId, out containerIds))
            {
                containerIds = new List<Guid>();
                result[row.BeneficiaryId] = containerIds;
            }
            containerIds.Add(row.ContainerId);
        }
        return result;
    }

    /// <remarks>
    /// Xóa MỌI thay đổi chưa lưu của cả request, không chỉ của Container. Chỉ gọi khi use case
    /// sẽ trả kết quả mà không SaveChanges nữa (ví dụ sau khi TryAddAssetLinkAsync trả false).
    /// </remarks>
    public void DiscardPendingChanges()
    {
        context.ChangeTracker.Clear();
    }
}
