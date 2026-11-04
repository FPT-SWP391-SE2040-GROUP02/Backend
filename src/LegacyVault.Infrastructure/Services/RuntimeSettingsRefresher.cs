using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Services;

/// <summary>
/// Nạp tham số Admin vào <see cref="RuntimeSettings"/> khi khởi động rồi cứ vài giây một lần, để API nhiều instance
/// và các job nền đều theo cấu hình mới mà không cần khởi động lại.
/// </summary>
public sealed class RuntimeSettingsRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<RuntimeSettingsRefresher> logger;

    public RuntimeSettingsRefresher(IServiceScopeFactory scopeFactory, ILogger<RuntimeSettingsRefresher> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    /// <summary>Đọc các tham số đang bật từ DB và thay ảnh chụp. Lỗi DB giữ nguyên ảnh cũ.</summary>
    public static async Task LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.IsEnabled)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            map[row.Key] = row.Value;
        }
        RuntimeSettings.Replace(map);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    await LoadAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Nạp tham số Admin lỗi, giữ cấu hình cũ: {ExceptionType}", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
