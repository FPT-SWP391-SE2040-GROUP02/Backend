namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Tiến độ của một lượt bàn giao: một gói cho một người nhận.</summary>
public class HandoverProgressTurnResponse
{
    public Guid TurnId { get; set; }

    /// <summary>Mã gói, để màn hình Tài sản gắn lượt vào đúng gói (hai gói có thể trùng tên).</summary>
    public Guid ContainerId { get; set; }

    /// <summary>Tên gói lúc mở bàn giao.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Mã hồ sơ người nhận trong kho (không phải tài khoản). Một người nhận nhiều gói thì các lượt cùng mã,
    /// để màn hình gom lượt theo người; không dùng 4 số cuối CCCD vì hai người có thể trùng.
    /// </summary>
    public Guid BeneficiaryId { get; set; }

    /// <summary>Chỉ 4 số cuối CCCD để Executor phân biệt người nhận; họ tên và số điện thoại trả riêng cho lượt còn hiệu lực hoặc đã bàn giao (xem BeneficiaryFullName).</summary>
    public string BeneficiaryCitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>
    /// Họ tên người nhận, để Executor liên hệ. Có ở lượt còn hiệu lực (INVITED, INITIAL_RESPONSE_EXPIRED,
    /// IDENTITY_IN_PROGRESS, READY_TO_ACCEPT) và lượt đã bàn giao (HANDOVER_COMMITTED); lượt đã kết thúc luôn null.
    /// </summary>
    public string? BeneficiaryFullName { get; set; }

    /// <summary>Số điện thoại liên hệ của người nhận. Cùng điều kiện với BeneficiaryFullName; null khi người nhận không có số hoặc không giải mã được.</summary>
    public string? BeneficiaryPhone { get; set; }

    /// <summary>
    /// Email người nhận để Executor biết lời mời gửi đi đâu. Cùng điều kiện với BeneficiaryPhone: chỉ có ở lượt còn hiệu lực
    /// hoặc đã bàn giao; null khi người nhận không có email hoặc không giải mã được.
    /// </summary>
    public string? BeneficiaryEmail { get; set; }

    /// <summary>Trạng thái lượt dạng UPPER_SNAKE_CASE, ví dụ INVITED, HANDOVER_COMMITTED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>true: lượt do chính tài khoản đang xem nhận. Executor không tự xác nhận được lượt này (BR-122).</summary>
    public bool ClaimedByYou { get; set; }

    /// <summary>true: một Executor đã xác nhận đúng người bằng tay thay cho eKYC (BR-122).</summary>
    public bool IdentityConfirmedByExecutor { get; set; }

    /// <summary>Lúc người nhận xác nhận đã nhận đủ tài sản; null khi chưa.</summary>
    public DateTimeOffset? ReceivedAllAt { get; set; }

    public DateTimeOffset InvitedAt { get; set; }

    /// <summary>Lúc máy chủ email nhận thư mời; null nghĩa là lời mời chưa gửi được (INVITE-04).</summary>
    public DateTimeOffset? InvitationSentAt { get; set; }

    /// <summary>Lúc người nhận ký biên bản (đồng ý hoặc từ chối) của lượt; null khi chưa có biên bản hoặc chưa ký.</summary>
    public DateTimeOffset? RecipientSignedAt { get; set; }

    /// <summary>Lúc Người xét duyệt ký biên bản (đồng ý hoặc từ chối) của lượt; null khi chưa có biên bản hoặc chưa ký.</summary>
    public DateTimeOffset? DocumentApprovedAt { get; set; }

    /// <summary>Lúc người nhận nhập đúng mã mở gói; null khi chưa nhập hoặc kho không dùng mã.</summary>
    public DateTimeOffset? KeyVerifiedAt { get; set; }

    public DateTimeOffset ResponseDeadline { get; set; }

    /// <summary>Lúc người nhận nhận lượt; null khi chưa ai nhận.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>Hạn đồng ý hoặc từ chối; null khi lượt chưa tới bước quyết định.</summary>
    public DateTimeOffset? DecisionDeadline { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Trạng thái quyền xem và tải (ACTIVE, BLOCKED, EXPIRED, REVOKED); null khi chưa có quyền.</summary>
    public string? GrantStatus { get; set; }

    public DateTimeOffset? GrantExpiresAt { get; set; }

    public int TotalItems { get; set; }

    /// <summary>Số tài sản người nhận đã tải thành công ít nhất một lần.</summary>
    public int DownloadedItems { get; set; }

    public List<HandoverProgressItemResponse> Items { get; set; } = new List<HandoverProgressItemResponse>();
}
