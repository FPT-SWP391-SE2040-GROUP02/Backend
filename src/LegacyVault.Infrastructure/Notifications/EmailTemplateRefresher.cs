using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Nạp chữ thư Admin đã sửa lúc khởi động rồi làm mới mỗi phút, để nhiều máy chủ cùng thấy bản mới. Lỗi DB chỉ ghi log, thư dùng bản đang có.</summary>
public sealed class EmailTemplateRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<EmailTemplateRefresher> logger;

    public EmailTemplateRefresher(IServiceScopeFactory scopeFactory, ILogger<EmailTemplateRefresher> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                IEmailTemplateService service = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();
                await service.RefreshSnapshotAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogWarning(failure, "Không nạp được chữ thư của Admin; giữ bản hiện có.");
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
