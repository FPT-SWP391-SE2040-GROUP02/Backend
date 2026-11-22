using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Vòng lặp nền gọi IHandoverJob theo chu kỳ để xử lý lượt và grant quá hạn.
/// </summary>
/// <remarks>
/// Chờ hết một chu kỳ rồi mới quét; mỗi mục một DI scope và try/catch riêng;
/// chỉ log kiểu exception và id.
/// </remarks>
public sealed class HandoverMonitor : BackgroundService
{
    /// <summary>Ba loại mốc hết hạn mà vòng quét xử lý, theo đúng ba nhóm của IHandoverJob.</summary>
    private enum ExpiryKind
    {
        Response,
        Decision,
        Grant
    }

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
                logger.LogError(ex, "Vòng quét bàn giao lỗi: {Message}", ex.Message);
            }
        }
    }

    /// <summary>Một lần quét: xử lý ba loại mốc hết hạn, rồi gửi lại các lời mời chưa giao được.</summary>
    private async Task RunOnceAsync(CancellationToken ct)
    {
        await RunKindAsync(ExpiryKind.Response, "phản hồi", ct);
        await RunKindAsync(ExpiryKind.Decision, "quyết định", ct);
        await RunKindAsync(ExpiryKind.Grant, "grant", ct);
        await RunInvitationRetryAsync(ct);
    }

    /// <summary>Gửi lại lời mời F3 chưa giao được (lần gửi đầu lỗi hoặc tiến trình dừng trước khi gửi).</summary>
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

    /// <summary>
    /// Xử lý một loại mốc hết hạn: tìm các id quá hạn trong một phạm vi riêng, rồi xử lý từng id trong phạm vi riêng của nó.
    /// Một id lỗi chỉ được ghi log, không làm dừng các id còn lại.
    /// </summary>
    private async Task RunKindAsync(ExpiryKind kind, string label, CancellationToken ct)
    {
        IReadOnlyList<Guid> ids;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IHandoverJob job = scope.ServiceProvider.GetRequiredService<IHandoverJob>();
            ids = await FindExpiredIdsAsync(job, kind, ct);
        }

        foreach (Guid id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    IHandoverJob job = scope.ServiceProvider.GetRequiredService<IHandoverJob>();
                    await ProcessExpiredAsync(job, kind, id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Xử lý hạn {Kind} của {Id} lỗi: {ExceptionType}", label, id, ex.GetType().Name);
            }
        }
    }

    /// <summary>Hỏi job danh sách id đã quá hạn của đúng loại.</summary>
    private static async Task<IReadOnlyList<Guid>> FindExpiredIdsAsync(IHandoverJob job, ExpiryKind kind, CancellationToken ct)
    {
        if (kind == ExpiryKind.Response)
        {
            return await job.FindExpiredResponseTurnIdsAsync(ct);
        }
        if (kind == ExpiryKind.Decision)
        {
            return await job.FindExpiredDecisionTurnIdsAsync(ct);
        }
        return await job.FindExpiredGrantIdsAsync(ct);
    }

    /// <summary>Gọi job xử lý một id quá hạn của đúng loại.</summary>
    private static async Task ProcessExpiredAsync(IHandoverJob job, ExpiryKind kind, Guid id, CancellationToken ct)
    {
        if (kind == ExpiryKind.Response)
        {
            await job.ProcessResponseExpiryAsync(id, ct);
        }
        else if (kind == ExpiryKind.Decision)
        {
            await job.ProcessDecisionExpiryAsync(id, ct);
        }
        else
        {
            await job.ProcessGrantExpiryAsync(id, ct);
        }
    }
}
