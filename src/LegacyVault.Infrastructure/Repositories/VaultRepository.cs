using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class VaultRepository(AppDbContext context) : IVaultRepository
{
    public Task<Vault?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return WithPlanAndEntitlement().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public Task<Vault?> FindByOwnerIdAsync(Guid ownerId, CancellationToken ct)
    {
        return WithPlanAndEntitlement().FirstOrDefaultAsync(v => v.OwnerId == ownerId, ct);
    }

    public async Task<bool> TryAddAsync(Vault vault, CancellationToken ct)
    {
        // Vault + Plan + Entitlement INSERT trong cùng một SaveChanges = một transaction (STATE-02).
        context.Vaults.Add(vault);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            DbExceptions.DetachAll(context, vault, vault.Plan, vault.Entitlement);
            return false;
        }
    }

    public async Task<long> SumUsedBytesAsync(Guid vaultId, CancellationToken ct)
    {
        // QUOTA-01/02: mọi phiên bản đang lưu đều chiếm dung lượng, kể cả khi tài sản không ở gói nào.
        return await context.AssetVersions
            .Where(v => v.Asset.VaultId == vaultId)
            .SumAsync(v => (long?)v.SizeBytes, ct) ?? 0;
    }

    private IQueryable<Vault> WithPlanAndEntitlement()
    {
        return context.Vaults.Include(v => v.Plan).Include(v => v.Entitlement).Include(v => v.Executors);
    }
}
