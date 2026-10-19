using LegacyVault.Application.Notifications;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Bộ gửi thư báo Executor giả: ghi lại thư đã gửi.</summary>
public sealed class FakeDeathCaseExecutorNoticeSender : IDeathCaseExecutorNoticeSender
{
    public List<DeathCaseExecutorNotice> Sent { get; } = new List<DeathCaseExecutorNotice>();

    public Task SendAsync(DeathCaseExecutorNotice notice, CancellationToken ct)
    {
        Sent.Add(notice);
        return Task.CompletedTask;
    }
}
