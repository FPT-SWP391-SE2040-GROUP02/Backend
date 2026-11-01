using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Dùng context.Set&lt;T&gt;() thay vì DbSet trên AppDbContext để không sửa AppDbContext (giống DeathCaseRepository).
/// </remarks>
public sealed class VerifierSigningGuardRepository : IVerifierSigningGuardRepository
{
    private readonly AppDbContext context;

    public VerifierSigningGuardRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<VerifierSigningGuard?> FindGuardAsync(Guid userId, CancellationToken ct)
    {
        return await context.Set<VerifierSigningGuard>()
            .Where(g => g.UserId == userId)
            .FirstOrDefaultAsync(ct);
    }

    public void AddGuard(VerifierSigningGuard guard)
    {
        context.Set<VerifierSigningGuard>().Add(guard);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        DbUpdateException? dbException = exception as DbUpdateException;
        if (dbException == null)
        {
            return false;
        }
        return DbExceptions.IsUniqueViolation(dbException);
    }
}
