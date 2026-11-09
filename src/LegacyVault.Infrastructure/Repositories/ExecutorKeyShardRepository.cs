using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Dùng Set&lt;ExecutorKeyShard&gt;() thay vì thêm DbSet vào AppDbContext để không sửa file dùng chung.</remarks>
public sealed class ExecutorKeyShardRepository : IExecutorKeyShardRepository
{
    private readonly AppDbContext context;

    public ExecutorKeyShardRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<ExecutorKeyShard?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<ExecutorKeyShard>().FirstOrDefaultAsync(s => s.VaultId == vaultId, ct);
    }

    public void Add(ExecutorKeyShard shard)
    {
        context.Set<ExecutorKeyShard>().Add(shard);
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
