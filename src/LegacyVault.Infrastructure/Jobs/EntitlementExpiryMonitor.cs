using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Vòng lặp nền gọi IEntitlementExpiryJob theo chu kỳ: kho trả phí hết hạn thì sang ân hạn chỉ đọc (OPLAN-01).
/// </summary>
/// <remarks>
/// - Cùng mẫu với CheckInMonitor: chờ hết một chu kỳ rồi mới quét lần đầu.
/// - Mỗi kho xử lý trong một DI scope (một DbContext) riêng và bọc try/catch: một kho lỗi không ảnh hưởng kho khác.
/// - Chỉ log kiểu exception và vaultId; không log thông tin cá nhân (OPS-04).
/// </remarks>
public sealed class EntitlementExpiryMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly EntitlementExpiryJobSettings settings;
    private readonly ILogger<EntitlementExpiryMonitor> logger;

    public EntitlementExpiryMonitor(
        IServiceScopeFactory scopeFactory,
        IOptions<EntitlementExpiryJobSettings> settings,
        ILogger<EntitlementExpiryMonitor> logger)
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
                logger.LogError(ex, "Vòng quét hết hạn dịch vụ lỗi: {Message}", ex.Message);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> vaultIds;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IEntitlementExpiryJob job = scope.ServiceProvider.GetRequiredService<IEntitlementExpiryJob>();
            vaultIds = await job.FindExpiredVaultIdsAsync(ct);
        }

        foreach (Guid vaultId in vaultIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    IEntitlementExpiryJob job = scope.ServiceProvider.GetRequiredService<IEntitlementExpiryJob>();
                    await job.ProcessVaultAsync(vaultId, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Ví dụ trùng lúc với webhook gia hạn (EDIT_CONFLICT): bỏ qua, vòng sau đánh giá lại.
                logger.LogWarning("Xử lý hết hạn dịch vụ của kho {VaultId} lỗi: {ExceptionType}", vaultId, ex.GetType().Name);
            }
        }
    }
}
