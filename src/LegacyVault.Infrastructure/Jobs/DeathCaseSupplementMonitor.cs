using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>Vòng lặp nền gọi IDeathCaseSupplementJob (cùng mẫu với DeathCaseNoticeMonitor).</summary>
public sealed class DeathCaseSupplementMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly DeathCaseJobSettings settings;
    private readonly ILogger<DeathCaseSupplementMonitor> logger;

    public DeathCaseSupplementMonitor(
        IServiceScopeFactory scopeFactory, IOptions<DeathCaseJobSettings> settings, ILogger<DeathCaseSupplementMonitor> logger)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.SupplementJobEnabled)
        {
            return;
        }
        int pollSeconds = settings.SupplementPollSeconds;
        if (pollSeconds < 1)
        {
            pollSeconds = 300;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                List<Guid> ids;
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    ids = await scope.ServiceProvider.GetRequiredService<IDeathCaseSupplementJob>().FindOverdueCaseIdsAsync(stoppingToken);
                }
                foreach (Guid id in ids)
                {
                    try
                    {
                        using (IServiceScope scope = scopeFactory.CreateScope())
                        {
                            await scope.ServiceProvider.GetRequiredService<IDeathCaseSupplementJob>().ProcessCaseAsync(id, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Ghi nhận quá hạn bổ sung hồ sơ {CaseId} lỗi: {ExceptionType}", id, ex.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Vòng quét quá hạn bổ sung lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
