namespace LegacyVault.Application.Notifications;

/// <summary>Returns true only after delivery acknowledgement. Never logs a token or destination.</summary>
public interface IExecutorInvitationSender
{
    Task<bool> SendAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken ct);
}
