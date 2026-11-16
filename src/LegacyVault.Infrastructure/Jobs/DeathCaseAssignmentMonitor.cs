using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Vòng lặp nền gọi IDeathCaseAssignmentJob theo chu kỳ để giao Verifier cho hồ sơ chứng tử chưa có người,
/// nhắc Verifier sắp quá hạn và giao lại khi quá hạn hoặc Verifier mất quyền (ASSIGN-04, TIME-05, NFR-03, D6).
/// </summary>
/// <remarks>
/// Chờ hết một chu kỳ rồi mới quét; mỗi hồ sơ một DI scope
/// và try/catch riêng; chỉ log kiểu exception và id (OPS-04). Chỉ Reassigned và ReminderFailed được log;
/// NoCandidate, Conflict, None không log để tránh đầy log mỗi phút.
/// </remarks>
public sealed class DeathCaseAssignmentMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly DeathCaseJobSettings settings;
    private readonly ILogger<DeathCaseAssignmentMonitor> logger;

    public DeathCaseAssignmentMonitor(
        IServiceScopeFactory scopeFactory,
        IOptions<DeathCaseJobSettings> settings,
        ILogger<DeathCaseAssignmentMonitor> logger)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.AssignmentJobEnabled)
        {
            return;
        }

        int pollSeconds = settings.AssignmentPollSeconds;
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
                logger.LogError(ex, "Vòng quét phân công Verifier hồ sơ chứng tử lỗi: {Message}", ex.Message);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> ids;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
            ids = await job.FindCaseIdsAsync(ct);
        }

        foreach (Guid id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
                    DeathCaseAssignmentOutcome outcome = await job.ProcessCaseAsync(id, ct);
                    if (outcome == DeathCaseAssignmentOutcome.Reassigned)
                    {
                        logger.LogInformation("Hồ sơ chứng tử {CaseId} đã được giao lại cho Verifier khác.", id);
                    }
                    else if (outcome == DeathCaseAssignmentOutcome.ReminderFailed)
                    {
                        logger.LogWarning("Gửi thư nhắc Verifier hồ sơ chứng tử {CaseId} lỗi, vòng sau thử lại.", id);
                    }
                    else if (outcome == DeathCaseAssignmentOutcome.NoCandidate)
                    {
                        // ASSIGN-04: không có Verifier phù hợp thì hồ sơ vẫn chờ, vòng sau thử giao lại.
                        logger.LogWarning("Hồ sơ chứng tử {CaseId} chưa có Verifier phù hợp để giao.", id);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Xử lý phân công Verifier hồ sơ chứng tử {CaseId} lỗi: {ExceptionType}", id, ex.GetType().Name);
            }
        }
    }
}
