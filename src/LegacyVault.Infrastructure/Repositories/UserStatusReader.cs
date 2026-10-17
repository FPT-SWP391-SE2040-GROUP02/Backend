using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class UserStatusReader : IUserStatusReader
{
    private readonly AppDbContext context;

    public UserStatusReader(AppDbContext context)
    {
        this.context = context;
    }

    public Task<UserStatus?> FindStatusAsync(Guid userId, CancellationToken ct)
    {
        // Chỉ lấy một cột Status: không nạp cả User vào DbContext của request.
        return context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (UserStatus?)u.Status)
            .FirstOrDefaultAsync(ct);
    }
}
