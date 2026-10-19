using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Vòng lặp nền gọi IHandoverJob theo chu kỳ để xử lý lượt và grant quá hạn (TIME-05).
/// </summary>
/// <remarks>
/// Cùng mẫu với CheckInMonitor: chờ hết một chu kỳ rồi mới quét; mỗi mục một DI scope và try/catch riêng;
/// chỉ log kiểu exception và id (OPS-04).
/// </remarks>
public sealed class HandoverMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly HandoverJobSettings settings;
    private readonly ILogger<HandoverMonitor> logger;

    public HandoverMonitor(
        IServiceScopeFactory scopeFactory,
        IOptions<HandoverJobSettings> settings,
        ILogger<HandoverMonitor> logger)
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
                logger.LogError("Vòng quét bàn giao lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await RunKindAsync("phản hồi", (job, token) => job.FindExpiredResponseTurnIdsAsync(token),
            (job, id, token) => job.ProcessResponseExpiryAsync(id, token), ct);
        await RunKindAsync("quyết định", (job, token) => job.FindExpiredDecisionTurnIdsAsync(token),
            (job, id, token) => job.ProcessDecisionExpiryAsync(id, token), ct);
        await RunKindAsync("grant", (job, token) => job.FindExpiredGrantIdsAsync(token),
            (job, id, token) => job.ProcessGrantExpiryAsync(id, token), ct);
        await RunInvitationRetryAsync(ct);
    }

    /// <summary>OPS-04: gửi lại lời mời F3 chưa giao được (lần gửi đầu lỗi hoặc tiến trình dừng trước khi gửi).</summary>
    private async Task RunInvitationRetryAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> ids;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IHandoverInvitationRetryJob job = scope.ServiceProvider.GetRequiredService<IHandoverInvitationRetryJob>();
            ids = await job.FindPendingTurnIdsAsync(ct);
        }

        foreach (Guid id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    IHandoverInvitationRetryJob job = scope.ServiceProvider.GetRequiredService<IHandoverInvitationRetryJob>();
                    await job.ProcessTurnAsync(id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Gửi lại lời mời của lượt {Id} lỗi: {ExceptionType}", id, ex.GetType().Name);
            }
        }
    }

    private async Task RunKindAsync(
        string kind,
        Func<IHandoverJob, CancellationToken, Task<IReadOnlyList<Guid>>> find,
        Func<IHandoverJob, Guid, CancellationToken, Task> process,
        CancellationToken ct)
    {
        IReadOnlyList<Guid> ids;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IHandoverJob job = scope.ServiceProvider.GetRequiredService<IHandoverJob>();
            ids = await find(job, ct);
        }

        foreach (Guid id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    IHandoverJob job = scope.ServiceProvider.GetRequiredService<IHandoverJob>();
                    await process(job, id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Xử lý hạn {Kind} của {Id} lỗi: {ExceptionType}", kind, id, ex.GetType().Name);
            }
        }
    }
}
