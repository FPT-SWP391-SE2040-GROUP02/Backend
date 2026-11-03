using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Đọc cấu hình dịch vụ ngoài: dòng DB (đã giải mã bí mật) đè appsettings. Cache 30 giây trong bộ nhớ,
/// nên instance khác có thể thấy giá trị mới trễ tối đa bằng chừng đó.
/// </summary>
public sealed class SystemConfigResolver : ISystemConfigResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly ISystemConfigRepository repository;
    private readonly ISystemConfigSecretProtector protector;
    private readonly ISystemConfigDefaults defaults;
    private readonly IMemoryCache cache;

    public SystemConfigResolver(
        ISystemConfigRepository repository,
        ISystemConfigSecretProtector protector,
        ISystemConfigDefaults defaults,
        IMemoryCache cache)
    {
        this.repository = repository;
        this.protector = protector;
        this.defaults = defaults;
        this.cache = cache;
    }

    public async Task<ResolvedConfig?> ResolveAsync(string serviceKey, SystemConfigSlot slot, CancellationToken ct)
    {
        string key = CacheKey(serviceKey, slot);
        object? hit = null;
        if (cache.TryGetValue(key, out hit) && hit is CachedConfig cached)
        {
            return cached.Value;
        }

        ResolvedConfig? loaded = await LoadAsync(serviceKey, slot, ct);
        cache.Set(key, new CachedConfig(loaded), CacheTtl);
        return loaded;
    }

    public void Invalidate(string serviceKey)
    {
        cache.Remove(CacheKey(serviceKey, SystemConfigSlot.Primary));
        cache.Remove(CacheKey(serviceKey, SystemConfigSlot.Fallback));
    }

    private async Task<ResolvedConfig?> LoadAsync(string serviceKey, SystemConfigSlot slot, CancellationToken ct)
    {
        SystemConfig? row = await repository.FindAsync(serviceKey, slot, ct);
        if (slot == SystemConfigSlot.Fallback && (row == null || !row.Enabled))
        {
            return null;
        }
        if (row != null)
        {
            return FromRow(serviceKey, row);
        }
        return defaults.ReadPrimary(serviceKey);
    }

    private ResolvedConfig FromRow(string serviceKey, SystemConfig row)
    {
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.Database;
        config.Settings = SystemConfigJson.ReadSettings(serviceKey, row.SettingsJson);
        config.Secrets = protector.Unprotect(row);
        return config;
    }

    private static string CacheKey(string serviceKey, SystemConfigSlot slot)
    {
        return "service-config:" + serviceKey + ":" + slot;
    }

    /// <summary>Bọc giá trị để cache được cả kết quả null (ô chưa có).</summary>
    private sealed class CachedConfig
    {
        public CachedConfig(ResolvedConfig? value)
        {
            Value = value;
        }

        public ResolvedConfig? Value { get; }
    }
}
