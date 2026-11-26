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

    /// <summary>Tên Chủ tài sản đã mời.</summary>
    public string OwnerName { get; set; } = string.Empty;

    public string InviteeEmail { get; set; } = string.Empty;

    /// <summary>INVITED, DECLINED, EXPIRED hoặc REVOKED.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset InvitedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Lúc từ chối, hết hạn hoặc bị thu hồi; null khi còn chờ.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>True khi Chủ tài sản đã ký biên bản phân chia và lời mời ký được; false thì phải chờ Chủ tài sản ký.</summary>
    public bool WillReady { get; set; }

    /// <summary>Phiên bản Chủ tài sản ký gần nhất; gửi lại khi chấp nhận. Null khi WillReady = false.</summary>
    public int? WillVersionNo { get; set; }

    public DateTimeOffset? WillSignedAt { get; set; }

    /// <summary>Số tài sản trong kho; chỉ là số đếm, không có tên hay nội dung. 0 khi WillReady = false.</summary>
    public int AssetCount { get; set; }

    /// <summary>Số người nhận trên tờ; 0 khi Chủ tài sản không khai người nhận.</summary>
    public int RecipientCount { get; set; }
}
