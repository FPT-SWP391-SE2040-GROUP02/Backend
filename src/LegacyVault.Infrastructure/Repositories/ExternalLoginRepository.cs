using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class ExternalLoginRepository : IExternalLoginRepository
{
    private readonly AppDbContext _db;

    public ExternalLoginRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<ExternalLogin?> FindBySubjectAsync(LoginProvider provider, string subject, CancellationToken ct)
    {
        return _db.ExternalLogins
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ProviderSubject == subject, ct);
    }

    public Task<ExternalLogin?> FindByUserAsync(Guid userId, LoginProvider provider, CancellationToken ct)
    {
        return _db.ExternalLogins
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Provider == provider, ct);
    }

    public Task<List<ExternalLogin>> FindAllByUserAsync(Guid userId, CancellationToken ct)
    {
        return _db.ExternalLogins
            .Where(e => e.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<bool> TryAddAsync(ExternalLogin login, CancellationToken ct)
    {
        // Nếu login.User là user MỚI (chưa có trong DB) thì EF thêm luôn user đó trong cùng lần lưu.
        _db.ExternalLogins.Add(login);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Trùng index unique do request khác vừa liên kết/tạo trước: tháo các entity vừa thêm khỏi DbContext
            // để lần SaveChanges sau của request không thử ghi lại chúng.
            _db.Entry(login).State = EntityState.Detached;
            if (login.User != null && _db.Entry(login.User).State == EntityState.Added)
            {
                _db.Entry(login.User).State = EntityState.Detached;
            }
            return false;
        }
    }

    public void Remove(ExternalLogin login)
    {
        _db.ExternalLogins.Remove(login);
    }
}
