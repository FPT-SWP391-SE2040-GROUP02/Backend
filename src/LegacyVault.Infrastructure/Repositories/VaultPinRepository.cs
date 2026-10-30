using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Dùng Set&lt;VaultPin&gt;() thay vì thêm DbSet vào AppDbContext để không sửa file dùng chung.</remarks>
public sealed class VaultPinRepository : IVaultPinRepository
{
    private readonly AppDbContext context;

    public VaultPinRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<VaultPin?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<VaultPin>().FirstOrDefaultAsync(p => p.VaultId == vaultId, ct);
    }

    public void Add(VaultPin pin)
    {
        context.Set<VaultPin>().Add(pin);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        DbUpdateException? update = exception as DbUpdateException;
        if (update == null)
        {
            return false;
        }
        return DbExceptions.IsUniqueViolation(update);
    }

    public void DiscardPendingChanges()
    {
        context.ChangeTracker.Clear();
    }
}
