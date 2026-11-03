using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Cầu nối đồng bộ tới ISystemConfigResolver cho các lớp singleton có thuộc tính đồng bộ. Dùng chung cache của bộ giải
/// nên Invalidate sau khi Admin lưu có hiệu lực ngay. Cache hết hạn thì tạo một scope và chờ bộ giải (một lần mỗi 30 giây
/// mỗi dịch vụ); lỗi đọc DB (ví dụ bảng chưa có migration) thì dùng giá trị mặc định của appsettings để thanh toán không ngừng.
/// </summary>
public sealed class SyncConfigReader : ISyncConfigReader
{
    private readonly IServiceScopeFactory scopes;
    private readonly IMemoryCache cache;
    private readonly ISystemConfigDefaults defaults;
    private readonly ILogger<SyncConfigReader> logger;

    public SyncConfigReader(
        IServiceScopeFactory scopes,
        IMemoryCache cache,
        ISystemConfigDefaults defaults,
        ILogger<SyncConfigReader> logger)
    {
        this.scopes = scopes;
        this.cache = cache;
        this.defaults = defaults;
        this.logger = logger;
    }

    public ResolvedConfig? Read(string serviceKey)
    {
        ResolvedConfig? cached = null;
        if (SystemConfigResolver.TryGetCached(cache, serviceKey, SystemConfigSlot.Primary, out cached))
        {
            return cached;
        }

        try
        {
            using (IServiceScope scope = scopes.CreateScope())
            {
                ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
                return resolver.ResolveAsync(serviceKey, SystemConfigSlot.Primary, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Không đọc được cấu hình {ServiceKey} từ DB ({ErrorType}), dùng cấu hình mặc định của máy chủ.",
                serviceKey, ex.GetType().Name);
            return defaults.ReadPrimary(serviceKey);
        }
    }
}
