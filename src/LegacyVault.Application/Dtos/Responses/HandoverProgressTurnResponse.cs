namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Tiến độ của một lượt bàn giao: một gói cho một người nhận.</summary>
public class HandoverProgressTurnResponse
{
    public Guid TurnId { get; set; }

    /// <summary>Tên gói lúc mở bàn giao.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Chỉ 4 số cuối CCCD để Executor phân biệt người nhận; họ tên và số điện thoại trả riêng cho lượt còn hiệu lực hoặc đã bàn giao (xem BeneficiaryFullName).</summary>
    public string BeneficiaryCitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>
    /// Họ tên người nhận, để Executor liên hệ. Có ở lượt còn hiệu lực (INVITED, INITIAL_RESPONSE_EXPIRED,
    /// IDENTITY_IN_PROGRESS, READY_TO_ACCEPT) và lượt đã bàn giao (HANDOVER_COMMITTED); lượt đã kết thúc luôn null.
    /// </summary>
    public string? BeneficiaryFullName { get; set; }

    /// <summary>Số điện thoại liên hệ của người nhận. Cùng điều kiện với BeneficiaryFullName; null khi người nhận không có số hoặc không giải mã được.</summary>
    public string? BeneficiaryPhone { get; set; }

    /// <summary>Trạng thái lượt dạng UPPER_SNAKE_CASE, ví dụ INVITED, HANDOVER_COMMITTED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>true: lượt do chính tài khoản đang xem nhận. Executor không tự xác nhận được lượt này (BR-122).</summary>
    public bool ClaimedByYou { get; set; }

    /// <summary>true: một Executor đã xác nhận đúng người bằng tay thay cho eKYC (BR-122).</summary>
    public bool IdentityConfirmedByExecutor { get; set; }

    public DateTimeOffset InvitedAt { get; set; }

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
