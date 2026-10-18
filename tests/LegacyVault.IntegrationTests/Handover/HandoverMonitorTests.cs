using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Vòng lặp nền HandoverMonitor với job giả (không cần DB).</summary>
public sealed class HandoverMonitorTests
{
    private static readonly Guid FailingTurnId = Guid.NewGuid();
    private static readonly Guid OkTurnId = Guid.NewGuid();
    private static readonly Guid DecisionTurnId = Guid.NewGuid();
    private static readonly Guid GrantId = Guid.NewGuid();

    private sealed class FakeHandoverJob : IHandoverJob
    {
        private readonly object gate = new object();
        public int FindCalls { get; private set; }
        public List<Guid> Processed { get; } = new List<Guid>();

        public Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(CancellationToken ct)
        {
            lock (gate)
            {
                FindCalls++;
            }
            IReadOnlyList<Guid> ids = new List<Guid> { FailingTurnId, OkTurnId };
            return Task.FromResult(ids);
        }

        public Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(CancellationToken ct)
        {
            IReadOnlyList<Guid> ids = new List<Guid> { DecisionTurnId };
            return Task.FromResult(ids);
        }

        public Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(CancellationToken ct)
        {
            IReadOnlyList<Guid> ids = new List<Guid> { GrantId };
            return Task.FromResult(ids);
        }

        public Task ProcessResponseExpiryAsync(Guid turnId, CancellationToken ct)
        {
            Record(turnId);
            if (turnId == FailingTurnId)
            {
                throw new InvalidOperationException("Lượt này lỗi (giả lập).");
            }
            return Task.CompletedTask;
        }

        public Task ProcessDecisionExpiryAsync(Guid turnId, CancellationToken ct)
        {
            Record(turnId);
            return Task.CompletedTask;
        }

        public Task ProcessGrantExpiryAsync(Guid grantId, CancellationToken ct)
        {
            Record(grantId);
            return Task.CompletedTask;
        }

        public bool HasProcessed(Guid id)
        {
            lock (gate)
            {
                return Processed.Contains(id);
            }
        }

        private void Record(Guid id)
        {
            lock (gate)
            {
                Processed.Add(id);
            }
        }
    }

    private static HandoverMonitor CreateMonitor(FakeHandoverJob job, bool enabled, int pollSeconds)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IHandoverJob>(job);
        ServiceProvider provider = services.BuildServiceProvider();

        HandoverJobSettings settings = new HandoverJobSettings();
        settings.Enabled = enabled;
        settings.PollSeconds = pollSeconds;
        return new HandoverMonitor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            NullLogger<HandoverMonitor>.Instance);
    }

    [Fact]
    public async Task Monitor_Disabled_NeverCallsJob()
    {
        FakeHandoverJob job = new FakeHandoverJob();
        HandoverMonitor monitor = CreateMonitor(job, enabled: false, pollSeconds: 1);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(1500);
        await monitor.StopAsync(CancellationToken.None);

        Assert.Equal(0, job.FindCalls);
    }

    [Fact]
    public async Task Monitor_Enabled_ProcessesAllThreeKindsAndSurvivesOneFailure()
    {
        FakeHandoverJob job = new FakeHandoverJob();
        HandoverMonitor monitor = CreateMonitor(job, enabled: true, pollSeconds: 1);

        await monitor.StartAsync(CancellationToken.None);
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!job.HasProcessed(GrantId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Task stopTask = monitor.StopAsync(CancellationToken.None);
        Task finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(job.HasProcessed(FailingTurnId));
        Assert.True(job.HasProcessed(OkTurnId));
        Assert.True(job.HasProcessed(DecisionTurnId));
        Assert.True(job.HasProcessed(GrantId));
        Assert.Same(stopTask, finished);
    }
}
