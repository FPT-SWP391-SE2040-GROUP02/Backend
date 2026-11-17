using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Dùng Set&lt;WillVersion&gt;() như các repository khác.</remarks>
public sealed class WillVersionRepository : IWillVersionRepository
{
    private readonly AppDbContext context;

    public WillVersionRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<WillVersion?> FindLatestByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<WillVersion>()
            .Where(w => w.VaultId == vaultId)
            .OrderByDescending(w => w.VersionNo)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(WillVersion version)
    {
        context.Set<WillVersion>().Add(version);
    }
}
