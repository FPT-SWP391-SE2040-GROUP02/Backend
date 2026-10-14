using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Vòng lặp nền CheckInMonitor với job giả (không cần DB).</summary>
public sealed class CheckInMonitorTests
{
    private static readonly Guid FailingPeriodId = Guid.NewGuid();
    private static readonly Guid OkPeriodId = Guid.NewGuid();

    private sealed class FakeCheckInJob : ICheckInJob
    {
        private readonly object gate = new object();
        public int FindCalls { get; private set; }
        public List<Guid> Processed { get; } = new List<Guid>();

        public Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(CancellationToken ct)
        {
            lock (gate)
            {
                FindCalls++;
            }
            IReadOnlyList<Guid> ids = new List<Guid> { FailingPeriodId, OkPeriodId };
            return Task.FromResult(ids);
        }

        public Task ProcessPeriodAsync(Guid periodId, CancellationToken ct)
        {
            lock (gate)
            {
                Processed.Add(periodId);
            }
            if (periodId == FailingPeriodId)
            {
                throw new InvalidOperationException("Kỳ này lỗi (giả lập).");
            }
            return Task.CompletedTask;
        }

        public bool HasProcessed(Guid periodId)
        {
            lock (gate)
            {
                return Processed.Contains(periodId);
            }
        }
    }

    private static CheckInMonitor CreateMonitor(FakeCheckInJob job, bool enabled, int pollSeconds)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<ICheckInJob>(job);
        ServiceProvider provider = services.BuildServiceProvider();

        CheckInJobSettings settings = new CheckInJobSettings();
        settings.Enabled = enabled;
        settings.PollSeconds = pollSeconds;
        return new CheckInMonitor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            NullLogger<CheckInMonitor>.Instance);
    }

    [Fact]
    public async Task Monitor_Disabled_NeverCallsJob()
    {
        FakeCheckInJob job = new FakeCheckInJob();
        CheckInMonitor monitor = CreateMonitor(job, enabled: false, pollSeconds: 1);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(1500);
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(0, job.FindCalls);
    }

    [Fact]
    public async Task Monitor_Enabled_ProcessesEveryPeriodAndSurvivesOneFailure()
    {
        FakeCheckInJob job = new FakeCheckInJob();
        CheckInMonitor monitor = CreateMonitor(job, enabled: true, pollSeconds: 1);

        await monitor.StartAsync(CancellationToken.None);
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!job.HasProcessed(OkPeriodId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Task stopTask = monitor.StopAsync(CancellationToken.None);
        Task finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(job.HasProcessed(FailingPeriodId));
        Assert.True(job.HasProcessed(OkPeriodId));
        Assert.Same(stopTask, finished);
    }
}
