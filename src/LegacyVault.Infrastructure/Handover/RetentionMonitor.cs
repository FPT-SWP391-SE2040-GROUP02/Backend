using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Vòng lặp nền gọi IRetentionJob theo chu kỳ (cùng mẫu với các monitor khác).</summary>
public sealed class RetentionMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly RetentionJobSettings settings;
    private readonly ILogger<RetentionMonitor> logger;

    public RetentionMonitor(
        IServiceScopeFactory scopeFactory, IOptions<RetentionJobSettings> settings, ILogger<RetentionMonitor> logger)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
    }

    /// <summary>Vòng lặp chính: chờ hết một chu kỳ rồi quét một lần, lặp đến khi ứng dụng dừng. Lỗi một vòng chỉ ghi log rồi chạy tiếp.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            return;
        }
        int pollSeconds = settings.PollSeconds;
        if (pollSeconds < 1)
        {
            pollSeconds = 300;
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
                logger.LogError("Vòng quét lưu dữ liệu lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    /// <summary>Một lần quét: lấy một lô kế hoạch, xử lý từng kế hoạch trong phạm vi riêng. Kế hoạch lỗi chỉ ghi log, vòng sau kiểm lại.</summary>
    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> planIds;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            planIds = await scope.ServiceProvider.GetRequiredService<IRetentionJob>().FindPlanIdsAsync(ct);
        }
        foreach (Guid planId in planIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    RetentionRunResult result = await scope.ServiceProvider.GetRequiredService<IRetentionJob>()
                        .ProcessPlanAsync(planId, ct);
                    if (result.TurnsDeleted > 0 || result.VaultClosed)
                    {
                        logger.LogInformation("Đã dọn dữ liệu kế hoạch {PlanId}: {Turns} lượt, {Assets} tài sản, đóng kho {Closed}.",
                            planId, result.TurnsDeleted, result.AssetsShredded, result.VaultClosed);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Xung đột với thao tác nhận/claim đang chạy (EDIT_CONFLICT) thì vòng sau kiểm lại.
                logger.LogWarning("Xử lý lưu dữ liệu kế hoạch {PlanId} lỗi: {ExceptionType}", planId, ex.GetType().Name);
            }
        }
    }
}
