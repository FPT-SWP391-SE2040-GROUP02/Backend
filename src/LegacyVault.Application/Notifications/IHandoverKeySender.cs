namespace LegacyVault.Application.Notifications;

/// <summary>Gửi khóa mở gói cho người nhận. Cài đặt ở Infrastructure (email); gửi lỗi thì ném exception.</summary>
public interface IHandoverKeySender
{
    Task SendAsync(HandoverKeyMail mail, CancellationToken ct);
}
