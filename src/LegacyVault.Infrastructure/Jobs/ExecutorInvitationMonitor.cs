using LegacyVault.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Vòng lặp nền gọi IExecutorInvitationJob: hết hạn lời mời 7 ngày và nhắc ở giờ thứ 48 (SETUP-20).
/// Cấu hình: Executor:InvitationJobEnabled (mặc định true), Executor:InvitationPollSeconds (mặc định 60).
/// </summary>
public sealed class ExecutorInvitationMonitor(IServiceScopeFactory scopeFactory, IConfiguration config,
    ILogger<ExecutorInvitationMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Executor:InvitationJobEnabled", true))
        {
            return;
        }
        int pollSeconds = config.GetValue("Executor:InvitationPollSeconds", 60);
        if (pollSeconds < 1)
        {
            pollSeconds = 60;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                List<Guid> ids;
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    ids = await scope.ServiceProvider.GetRequiredService<IExecutorInvitationJob>().FindDueIdsAsync(stoppingToken);
                }
                foreach (Guid id in ids)
                {
                    try
                    {
                        using (IServiceScope scope = scopeFactory.CreateScope())
                        {
                            await scope.ServiceProvider.GetRequiredService<IExecutorInvitationJob>().ProcessAsync(id, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Xử lý lời mời Executor {ExecutorId} lỗi: {ExceptionType}", id, ex.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Vòng quét lời mời Executor lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
