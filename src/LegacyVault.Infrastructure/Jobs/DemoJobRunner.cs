using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Chạy một lượt mọi việc nền theo giờ hệ thống hiện tại, đúng cách các monitor nền làm (mỗi mục một DI scope riêng,
/// một mục lỗi không làm hỏng mục khác). Dùng cho nút "Tua thời gian": ở mỗi mốc cần xử lý, gọi một lượt này thay vì
/// chờ monitor quét theo chu kỳ.
/// </summary>
public sealed class DemoJobRunner : IDemoJobRunner
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DemoJobRunner> logger;

    public DemoJobRunner(IServiceScopeFactory scopeFactory, ILogger<DemoJobRunner> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    public async Task<DemoJobPassResult> RunAllJobsOnceAsync(CancellationToken ct)
    {
        DemoJobPassResult result = new DemoJobPassResult();
        int sentBefore = await CountSentRemindersAsync(ct);
        int executorReminders = 0;

        // Điểm danh: chuyển trạng thái quá hạn và gửi thư nhắc mốc mới nhất.
        await RunKindAsync("điểm danh", result,
            async (provider, token) =>
            {
                IReadOnlyList<Guid> ids = await provider.GetRequiredService<ICheckInJob>().FindOpenPeriodIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<ICheckInJob>().ProcessPeriodAsync(id, token);
                await provider.GetRequiredService<IOperationsRecorder>().RecordCheckInPeriodAsync(id, token);
            }, ct);

        // Thư bổ sung do Admin đặt, tính theo ngày từ hạn điểm danh.
        await RunKindAsync("thư bổ sung", result,
            async (provider, token) =>
            {
                IReadOnlyList<Guid> ids = await provider.GetRequiredService<IExtraMailJob>().FindPeriodIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IExtraMailJob>().ProcessPeriodAsync(id, token);
            }, ct);

        // Lời mời Người thực thi: hết hạn hoặc nhắc ở giờ thứ 48.
        await RunKindAsync("lời mời Người thực thi", result,
            async (provider, token) =>
            {
                List<Guid> ids = await provider.GetRequiredService<IExecutorInvitationJob>().FindDueIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                string? outcome = await provider.GetRequiredService<IExecutorInvitationJob>().ProcessAsync(id, token);
                if (outcome == "REMINDED")
                {
                    executorReminders++;
                }
            }, ct);

        // Hết hạn dịch vụ.
        await RunKindAsync("hết hạn dịch vụ", result,
            async (provider, token) =>
            {
                IReadOnlyList<Guid> ids = await provider.GetRequiredService<IEntitlementExpiryJob>().FindExpiredVaultIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IEntitlementExpiryJob>().ProcessVaultAsync(id, token);
            }, ct);

        // Bàn giao: lượt quá hạn phản hồi, quá hạn quyết định, grant hết hạn tải.
        await RunKindAsync("hạn phản hồi bàn giao", result,
            async (provider, token) =>
            {
                IReadOnlyList<Guid> ids = await provider.GetRequiredService<IHandoverJob>().FindExpiredResponseTurnIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IHandoverJob>().ProcessResponseExpiryAsync(id, token);
            }, ct);
        await RunKindAsync("hạn quyết định bàn giao", result,
            async (provider, token) =>
            {
                IReadOnlyList<Guid> ids = await provider.GetRequiredService<IHandoverJob>().FindExpiredDecisionTurnIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IHandoverJob>().ProcessDecisionExpiryAsync(id, token);
            }, ct);
        await RunKindAsync("hạn tải bàn giao", result,
            async (provider, token) =>
            {
                IReadOnlyList<Guid> ids = await provider.GetRequiredService<IHandoverJob>().FindExpiredGrantIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IHandoverJob>().ProcessGrantExpiryAsync(id, token);
            }, ct);

        // Hồ sơ chứng tử: thông báo cho Chủ kho, giao Người xét duyệt, quá hạn bổ sung.
        await RunKindAsync("thông báo hồ sơ chứng tử", result,
            async (provider, token) =>
            {
                List<Guid> ids = await provider.GetRequiredService<IDeathCaseNoticeJob>().FindPendingNoticeIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IDeathCaseNoticeJob>().ProcessNoticeAsync(id, token);
            }, ct);
        await RunKindAsync("giao Người xét duyệt", result,
            async (provider, token) =>
            {
                List<Guid> ids = await provider.GetRequiredService<IDeathCaseAssignmentJob>().FindCaseIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                DeathCaseAssignmentOutcome outcome =
                    await provider.GetRequiredService<IDeathCaseAssignmentJob>().ProcessCaseAsync(id, token);
                await provider.GetRequiredService<IOperationsRecorder>().RecordAssignmentOutcomeAsync(id, outcome, token);
            }, ct);
        await RunKindAsync("quá hạn bổ sung hồ sơ", result,
            async (provider, token) =>
            {
                List<Guid> ids = await provider.GetRequiredService<IDeathCaseSupplementJob>().FindOverdueCaseIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IDeathCaseSupplementJob>().ProcessCaseAsync(id, token);
            }, ct);

        // Sự cố vận hành: giao người, nhắc, báo lên Admin.
        await RunKindAsync("sự cố", result,
            async (provider, token) =>
            {
                List<Guid> ids = await provider.GetRequiredService<IIncidentJob>().FindIdsAsync(token);
                return ids;
            },
            async (provider, id, token) =>
            {
                await provider.GetRequiredService<IIncidentJob>().ProcessAsync(id, token);
            }, ct);

        int sentAfter = await CountSentRemindersAsync(ct);
        result.EmailsSent = (sentAfter - sentBefore) + executorReminders;
        return result;
    }

    private async Task<int> CountSentRemindersAsync(CancellationToken ct)
    {
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IDemoClockRepository repository = scope.ServiceProvider.GetRequiredService<IDemoClockRepository>();
            return await repository.CountSentCheckInRemindersAsync(ct);
        }
    }

    /// <summary>Tìm các mục cần xử lý rồi xử lý từng mục trong một scope riêng; lỗi một mục chỉ được đếm và ghi log.</summary>
    private async Task RunKindAsync(
        string label,
        DemoJobPassResult result,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<Guid>>> find,
        Func<IServiceProvider, Guid, CancellationToken, Task> process,
        CancellationToken ct)
    {
        IReadOnlyList<Guid> ids;
        try
        {
            using (IServiceScope scope = scopeFactory.CreateScope())
            {
                ids = await find(scope.ServiceProvider, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Tua thời gian: quét {Label} lỗi: {ExceptionType}", label, ex.GetType().Name);
            result.Failures++;
            return;
        }

        foreach (Guid id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    await process(scope.ServiceProvider, id, ct);
                }
                result.ItemsProcessed++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Tua thời gian: xử lý {Label} {Id} lỗi: {ExceptionType}", label, id, ex.GetType().Name);
                result.Failures++;
            }
        }
    }
}
