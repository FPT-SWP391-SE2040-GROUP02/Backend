namespace LegacyVault.Application.Notifications;

/// <summary>Gửi mã nhận cho người nhận. Cài đặt ở Infrastructure (email); gửi lỗi thì ném exception.</summary>
public interface IHandoverKeySender
{
    Task SendAsync(HandoverKeyMail mail, CancellationToken ct);
}
