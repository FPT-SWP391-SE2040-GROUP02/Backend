namespace LegacyVault.Application.Notifications;

/// <summary>Dữ liệu một lời mời nhận tài sản. Token gốc chỉ đi qua đây, không bao giờ lưu DB (chỉ lưu hash).</summary>
public sealed class HandoverInvitation
{
    public Guid TurnId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Token gốc để dựng link mời.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTimeOffset ResponseDeadline { get; set; }
}

/// <summary>
/// Gửi lời mời cho người nhận (INVITE-01). Cài đặt ở Infrastructure (email).
/// </summary>
/// <remarks>
/// OPS-04: email chỉ có tên và link, không có tên gói hay tên tài sản.
/// Gửi lỗi thì ném exception; HandoverStarter giữ lượt và để InvitationSentAt = null.
/// </remarks>
public interface IHandoverInvitationSender
{
    Task SendAsync(HandoverInvitation invitation, CancellationToken ct);
}
