using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        return _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<bool> TryAddAsync(User user, CancellationToken ct)
    {
        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Trùng email hoặc số điện thoại do hai request ghi cùng lúc: tháo entity khỏi DbContext để không làm bẩn phiên
            _db.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public Task<bool> PhoneExistsAsync(string normalizedPhone, Guid? exceptUserId, CancellationToken ct)
    {
        return _db.Users.AnyAsync(u => u.Phone == normalizedPhone && (exceptUserId == null || u.Id != exceptUserId), ct);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return exception is DbUpdateException update && DbExceptions.IsUniqueViolation(update);
    }
}
