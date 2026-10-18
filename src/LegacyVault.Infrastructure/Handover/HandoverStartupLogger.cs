using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Ghi một dòng log lúc khởi động cho biết xác minh danh tính của người nhận đang bật hay bị bỏ qua,
/// để không ai quên rằng môi trường đang chạy ở chế độ thử.
/// </summary>
public sealed class HandoverStartupLogger : IHostedService
{
    private readonly HandoverSettings settings;
    private readonly ILogger<HandoverStartupLogger> logger;

    public HandoverStartupLogger(IOptions<HandoverSettings> settings, ILogger<HandoverStartupLogger> logger)
    {
        this.settings = settings.Value;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (settings.RequireIdentityVerification)
        {
            logger.LogInformation("Bàn giao: người nhận phải qua xác minh danh tính trước khi xem tài sản.");
        }
        else
        {
            logger.LogWarning("Bàn giao: ĐANG BỎ QUA xác minh danh tính của người nhận "
                + "(Handover:RequireIdentityVerification=false, chỉ hợp lệ ở môi trường Development).");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
