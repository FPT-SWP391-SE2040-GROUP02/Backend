using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class EmailVerificationTokenRepository : IEmailVerificationTokenRepository
{
    private readonly AppDbContext _db;

    public EmailVerificationTokenRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<EmailVerificationToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
    {
        return _db.EmailVerificationTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public Task<List<EmailVerificationToken>> FindUnusedByUserAsync(Guid userId, CancellationToken ct)
    {
        return _db.EmailVerificationTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(ct);
    }

    public void Add(EmailVerificationToken token)
    {
        _db.EmailVerificationTokens.Add(token);
    }
}
