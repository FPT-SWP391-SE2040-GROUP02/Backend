namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một lời mời nhận gói đang chờ tài khoản đang đăng nhập (chưa ai nhận, đúng email đã xác minh).</summary>
public class PendingInvitationResponse
{
    public Guid TurnId { get; set; }

    public string ContainerName { get; set; } = string.Empty;

    public string OwnerFullName { get; set; } = string.Empty;

    public DateTimeOffset InvitedAt { get; set; }
}
