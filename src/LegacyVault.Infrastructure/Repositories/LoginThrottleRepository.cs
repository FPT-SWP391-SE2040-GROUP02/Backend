using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class LoginThrottleRepository : ILoginThrottleRepository
{
    private readonly AppDbContext _db;

    public LoginThrottleRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<LoginThrottle?> FindAsync(string normalizedEmail, string ipRange, CancellationToken ct)
    {
        return _db.LoginThrottles
            .FirstOrDefaultAsync(t => t.Email == normalizedEmail && t.IpRange == ipRange, ct);
    }

    public void Add(LoginThrottle throttle)
    {
        _db.LoginThrottles.Add(throttle);
    }

    public void Remove(LoginThrottle throttle)
    {
        _db.LoginThrottles.Remove(throttle);
    }
}
