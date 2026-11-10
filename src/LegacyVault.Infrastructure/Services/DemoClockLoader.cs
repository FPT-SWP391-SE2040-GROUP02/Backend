using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Services;

/// <summary>
/// Khi máy chủ khởi động, nạp độ lệch đồng hồ DEMO đã lưu trong DB để khởi động lại không làm mất ngày đang giả lập.
/// Lỗi đọc DB (ví dụ DB chưa có) chỉ ghi log và giữ giờ thật, không chặn khởi động.
/// </summary>
public sealed class DemoClockLoader : IHostedService
{
    private readonly IDemoClock clock;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DemoClockLoader> logger;

    public DemoClockLoader(IDemoClock clock, IServiceScopeFactory scopeFactory, ILogger<DemoClockLoader> logger)
    {
        this.clock = clock;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!clock.IsEnabled)
        {
            return;
        }
        try
        {
            using (IServiceScope scope = scopeFactory.CreateScope())
            {
                IDemoClockRepository repository = scope.ServiceProvider.GetRequiredService<IDemoClockRepository>();
                long? seconds = await repository.FindOffsetSecondsAsync(cancellationToken);
                if (seconds != null)
                {
                    clock.SetOffset(TimeSpan.FromSeconds(seconds.Value));
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Không nạp được độ lệch đồng hồ DEMO, dùng giờ thật: {ExceptionType}", ex.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
