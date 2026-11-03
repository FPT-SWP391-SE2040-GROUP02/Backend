namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Lời mời làm Executor gửi tới email của tài khoản đang đăng nhập (GET /executor-invitations/mine).
/// Không có token, không có nội dung kho; chỉ đủ để người được mời biết ai mời và trả lời.
/// </summary>
public sealed class MyExecutorInvitationsResponse
{
    /// <summary>Phiên bản phạm vi nhiệm vụ hiện hành; gửi lại khi trả lời.</summary>
    public string ScopeVersion { get; set; } = string.Empty;

    public string ScopeText { get; set; } = string.Empty;

    /// <summary>Lời mời còn hạn, chưa trả lời. Sắp hết hạn trước.</summary>
    public List<MyExecutorInvitationResponse> Pending { get; set; } = new List<MyExecutorInvitationResponse>();

    /// <summary>Lời mời đã từ chối, hết hạn hoặc bị thu hồi. Mới nhất trước.</summary>
    public List<MyExecutorInvitationResponse> Ended { get; set; } = new List<MyExecutorInvitationResponse>();
}

public sealed class MyExecutorInvitationResponse
{
    public Guid Id { get; set; }

    /// <summary>Tên Chủ kho đã mời.</summary>
    public string OwnerName { get; set; } = string.Empty;

    public string InviteeEmail { get; set; } = string.Empty;

    /// <summary>INVITED, DECLINED, EXPIRED hoặc REVOKED.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset InvitedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Lúc từ chối, hết hạn hoặc bị thu hồi; null khi còn chờ.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}
