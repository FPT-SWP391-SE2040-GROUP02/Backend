using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Vòng lặp nền gọi ICheckInJob theo chu kỳ để chuyển trạng thái và gửi nhắc điểm danh (TIME-05).
/// </summary>
/// <remarks>
/// - Chờ hết một chu kỳ rồi mới quét lần đầu (không chạy ngay lúc khởi động).
/// - Mỗi kỳ xử lý trong một DI scope (một DbContext) riêng và bọc try/catch: một kỳ lỗi không ảnh hưởng kỳ khác.
/// - Chỉ log kiểu exception và periodId; không log email hay nội dung (OPS-04).
/// </remarks>
public sealed class CheckInMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly CheckInJobSettings settings;
    private readonly ILogger<CheckInMonitor> logger;

    public CheckInMonitor(
        IServiceScopeFactory scopeFactory,
        IOptions<CheckInJobSettings> settings,
        ILogger<CheckInMonitor> logger)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            return;
        }

        int pollSeconds = settings.PollSeconds;
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
                logger.LogError(ex, "Vòng quét điểm danh lỗi: {Message}", ex.Message);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> periodIds;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            ICheckInJob job = scope.ServiceProvider.GetRequiredService<ICheckInJob>();
            periodIds = await job.FindOpenPeriodIdsAsync(ct);
        }

        foreach (Guid periodId in periodIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    ICheckInJob job = scope.ServiceProvider.GetRequiredService<ICheckInJob>();
                    await job.ProcessPeriodAsync(periodId, ct);
                    // Sang AWAITING_CHECK → sự cố giao Executor (DMS-05); không có Executor → "bị kẹt" có người và ngày xem lại.
                    IOperationsRecorder recorder = scope.ServiceProvider.GetRequiredService<IOperationsRecorder>();
                    await recorder.RecordCheckInPeriodAsync(periodId, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Xử lý kỳ điểm danh {PeriodId} lỗi: {ExceptionType}", periodId, ex.GetType().Name);
            }
        }
    }
}
