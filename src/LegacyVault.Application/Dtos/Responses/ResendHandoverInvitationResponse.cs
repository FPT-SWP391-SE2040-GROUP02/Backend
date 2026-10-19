namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả gửi lại lời mời bàn giao. Không có email hay link của người nhận (OPS-04).</summary>
public class ResendHandoverInvitationResponse
{
    public Guid TurnId { get; set; }

    /// <summary>Lần email được máy chủ nhận gần nhất.</summary>
    public DateTimeOffset InvitationSentAt { get; set; }

    /// <summary>Hạn phản hồi hiện tại; chỉ đặt lại khi đây là lần đầu lời mời được giao (INVITE-02, INVITE-04).</summary>
    public DateTimeOffset ResponseDeadline { get; set; }
}
