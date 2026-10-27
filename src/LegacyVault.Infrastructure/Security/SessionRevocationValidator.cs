using LegacyVault.Application.Security;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LegacyVault.Infrastructure.Security;

public sealed class SessionRevocationValidator : ISessionRevocationValidator
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    public SessionRevocationValidator(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsSessionActiveAsync(Guid familyId, CancellationToken ct = default)
    {
        var cacheKey = $"session_active:{familyId:D}";
        if (_cache.TryGetValue<bool>(cacheKey, out var cachedActive))
        {
            return cachedActive;
        }

        var isActive = await _db.RefreshTokens
            .AnyAsync(r => r.FamilyId == familyId && r.RevokedAt == null, ct);

        _cache.Set(cacheKey, isActive, CacheDuration);
        return isActive;
    }

    public void InvalidateFamily(Guid familyId)
    {
        _cache.Remove($"session_active:{familyId:D}");
    }
}
