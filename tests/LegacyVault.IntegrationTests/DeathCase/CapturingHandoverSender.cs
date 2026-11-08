using System.Collections.Concurrent;
using LegacyVault.Application.Notifications;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Bộ gửi lời mời F3 giả: giữ token để test nhận lượt.</summary>
public sealed class CapturingHandoverSender : IHandoverInvitationSender
{
    public ConcurrentQueue<HandoverInvitation> Invitations { get; } = new ConcurrentQueue<HandoverInvitation>();

    public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
    {
        Invitations.Enqueue(invitation);
        return Task.CompletedTask;
    }
}
