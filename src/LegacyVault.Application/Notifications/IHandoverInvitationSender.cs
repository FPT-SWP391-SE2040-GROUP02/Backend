namespace LegacyVault.Application.Notifications;

/// <summary>
/// Gửi lời mời cho người nhận. Cài đặt ở Infrastructure (email).
/// </summary>
/// <remarks>
/// Email chỉ có tên và link, không có tên gói hay tên tài sản.
/// Gửi lỗi thì ném exception; HandoverStarter giữ lượt và để InvitationSentAt = null.
/// </remarks>
public interface IHandoverInvitationSender
{
    Task SendAsync(HandoverInvitation invitation, CancellationToken ct);
}
