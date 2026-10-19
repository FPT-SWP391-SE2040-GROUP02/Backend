using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Dùng Set&lt;T&gt;() thay vì thêm DbSet vào AppDbContext để không sửa file dùng chung.</remarks>
public sealed class TwoFactorRepository : ITwoFactorRepository
{
    private readonly AppDbContext context;

    public TwoFactorRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<UserTotp?> FindTotpAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<UserTotp>().FirstOrDefaultAsync(t => t.UserId == userId, ct);
    }

    public void AddTotp(UserTotp totp)
    {
        context.Set<UserTotp>().Add(totp);
    }

    public void RemoveTotp(UserTotp totp)
    {
        context.Set<UserTotp>().Remove(totp);
    }

    public Task<List<TwoFactorRecoveryCode>> FindUnusedRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<TwoFactorRecoveryCode>()
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<List<TwoFactorRecoveryCode>> FindAllRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<TwoFactorRecoveryCode>()
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);
    }

    public void AddRecoveryCode(TwoFactorRecoveryCode code)
    {
        context.Set<TwoFactorRecoveryCode>().Add(code);
    }

    public void RemoveRecoveryCode(TwoFactorRecoveryCode code)
    {
        context.Set<TwoFactorRecoveryCode>().Remove(code);
    }

    public Task<TwoFactorChallenge?> FindChallengeByHashAsync(string tokenHash, CancellationToken ct)
    {
        return context.Set<TwoFactorChallenge>()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.TokenHash == tokenHash, ct);
    }

    public void AddChallenge(TwoFactorChallenge challenge)
    {
        context.Set<TwoFactorChallenge>().Add(challenge);
    }
}
