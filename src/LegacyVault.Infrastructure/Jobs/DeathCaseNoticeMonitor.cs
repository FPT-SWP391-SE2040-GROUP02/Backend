using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Vòng lặp nền gọi IDeathCaseNoticeJob theo chu kỳ để gửi thông báo hồ sơ chứng tử còn Pending (DEATH-08, D3).
/// </summary>
/// <remarks>
/// Cùng mẫu với CheckInMonitor/HandoverMonitor: chờ hết một chu kỳ rồi mới quét; mỗi thông báo một DI scope
/// và try/catch riêng; chỉ log kiểu exception và id (OPS-04).
/// </remarks>
public sealed class DeathCaseNoticeMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly DeathCaseJobSettings settings;
    private readonly ILogger<DeathCaseNoticeMonitor> logger;

    public DeathCaseNoticeMonitor(
        IServiceScopeFactory scopeFactory,
        IOptions<DeathCaseJobSettings> settings,
        ILogger<DeathCaseNoticeMonitor> logger)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.NoticeJobEnabled)
        {
            return;
        }

        int pollSeconds = settings.NoticePollSeconds;
        if (pollSeconds < 1)
        {
            pollSeconds = 60;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Vòng quét thông báo hồ sơ chứng tử lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> ids;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
            ids = await job.FindPendingNoticeIdsAsync(ct);
        }

        foreach (Guid id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
                    DeathCaseNoticeStatus? status = await job.ProcessNoticeAsync(id, ct);
                    if (status == DeathCaseNoticeStatus.Failed)
                    {
                        // TODO: F4 — tạo sự cố có người và hạn xử lý thay cho dòng log này (DEATH-08).
                        logger.LogWarning("Thông báo hồ sơ chứng tử {NoticeId} gửi lỗi đủ số lần thử, chuyển FAILED.", id);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Xử lý thông báo hồ sơ chứng tử {NoticeId} lỗi: {ExceptionType}", id, ex.GetType().Name);
            }
        }
    }
}
