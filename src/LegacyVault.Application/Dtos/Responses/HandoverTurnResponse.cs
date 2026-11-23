namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một lượt bàn giao nhìn từ phía người nhận: trạng thái, các hạn, điều kiện khóa mở gói, tiêu đề lượt và tài sản (khi đủ điều kiện), quyền tải.</summary>
public class HandoverTurnResponse
{
    public Guid Id { get; set; }

    /// <summary>Họ tên người nhận do Chủ kho chỉ định; null khi không đọc được. Chỉ trả cho chính người nhận của lượt.</summary>
    public string? RecipientFullName { get; set; }

    /// <summary>Tên trạng thái UPPER_SNAKE: INVITED, IDENTITY_IN_PROGRESS, READY_TO_ACCEPT, ...</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Giờ máy chủ, để trang web đếm ngược không phụ thuộc đồng hồ máy người dùng.</summary>
    public DateTimeOffset ServerTime { get; set; }

    public DateTimeOffset InvitedAt { get; set; }

    public DateTimeOffset ResponseDeadline { get; set; }

    public DateTimeOffset? DecisionDeadline { get; set; }

    /// <summary>true: xác minh danh tính đã được bỏ qua bằng công tắc cấu hình (chỉ Development).</summary>
    public bool IdentityBypassed { get; set; }

    /// <summary>true: một Executor đã xác nhận đúng người bằng tay thay cho eKYC.</summary>
    public bool IdentityConfirmedByExecutor { get; set; }

    /// <summary>true: kho đã cấp khóa mở gói, người nhận phải nhập khóa do Người thực thi đưa trước khi đồng ý.</summary>
    public bool KeyRequired { get; set; }

    /// <summary>true: người nhận đã nhập đúng khóa (hoặc không cần khóa).</summary>
    public bool KeyVerified { get; set; }

    /// <summary>Chỉ có khi lượt ở READY_TO_ACCEPT hoặc HANDOVER_COMMITTED.</summary>
    public HandoverContainerResponse? Container { get; set; }

    /// <summary>Chỉ có khi lượt ở READY_TO_ACCEPT hoặc HANDOVER_COMMITTED; ngược lại rỗng.</summary>
    public List<HandoverItemResponse> Items { get; set; } = new List<HandoverItemResponse>();

    public HandoverGrantResponse? Grant { get; set; }

    /// <summary>Người xét duyệt đã duyệt hồ sơ chứng tử của kho; người nhận thấy tên và số thẻ luật sư để tự kiểm chứng.</summary>
    public HandoverReviewerResponse? Reviewer { get; set; }

    /// <summary>Lúc người nhận xác nhận đã nhận đủ tài sản; null khi chưa.</summary>
    public DateTimeOffset? ReceivedAllAt { get; set; }

    /// <summary>Lúc Người thực thi ký phiếu giao nhận; null khi chưa.</summary>
    public DateTimeOffset? ReceiptExecutorSignedAt { get; set; }

    /// <summary>Người nhận báo thiếu hoặc sai; null khi không báo.</summary>
    public string? ReceiptIssueNote { get; set; }

    public DateTimeOffset? ReceiptIssueAt { get; set; }
}
