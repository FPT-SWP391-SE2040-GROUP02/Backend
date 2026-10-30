using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Dùng context.Set&lt;T&gt;() thay vì DbSet trên AppDbContext để không sửa AppDbContext (giống DeathCaseRepository).
/// </remarks>
public sealed class VerifierSigningKeyRepository : IVerifierSigningKeyRepository
{
    private readonly AppDbContext context;

    public VerifierSigningKeyRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<VerifierSigningKey?> FindActiveByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return await context.Set<VerifierSigningKey>()
            .Where(k => k.UserId == userId && k.RevokedAt == null)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<VerifierSigningKey?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return await context.Set<VerifierSigningKey>()
            .AsNoTracking()
            .Where(k => k.Id == id)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(VerifierSigningKey key)
    {
        context.Set<VerifierSigningKey>().Add(key);
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
}
