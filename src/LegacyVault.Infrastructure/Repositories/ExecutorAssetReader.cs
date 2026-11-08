using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ truy cập dữ liệu; luật ai được xem nằm ở ExecutorAssetService.</remarks>
public sealed class ExecutorAssetReader : IExecutorAssetReader
{
    private readonly AppDbContext context;

    public ExecutorAssetReader(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<List<Container>> FindReadyPackagesAsync(Guid vaultId, CancellationToken ct)
    {
        List<Container> packages = await context.Set<Container>()
            .AsNoTracking()
            .Include(c => c.Assets)
                .ThenInclude(link => link.Asset)
            .Include(c => c.Designations)
                .ThenInclude(d => d.Beneficiary)
            .Where(c => c.VaultId == vaultId && c.Status == ContainerStatus.Ready)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
        return packages;
    }
}
