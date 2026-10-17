using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Dùng Set&lt;PasswordResetToken&gt;() thay vì thêm DbSet vào AppDbContext để không sửa file dùng chung.</remarks>
public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly AppDbContext context;

    public PasswordResetTokenRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
    {
        return context.Set<PasswordResetToken>()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public Task<List<PasswordResetToken>> FindUnusedByUserAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<PasswordResetToken>()
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
    }

    public void Add(PasswordResetToken token)
    {
        context.Set<PasswordResetToken>().Add(token);
    }
}
