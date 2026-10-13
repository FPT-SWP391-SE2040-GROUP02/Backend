using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        return context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<bool> TryAddAsync(User user, CancellationToken ct)
    {
        context.Users.Add(user);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            DbExceptions.DetachAll(context, user);
            return false;
        }
    }
}
