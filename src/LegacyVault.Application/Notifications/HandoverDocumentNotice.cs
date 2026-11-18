namespace LegacyVault.Application.Notifications;

/// <summary>Thư báo về biên bản. Không có nội dung tài sản, số căn cước hay họ tên người khác ngoài tên cần để nhận biết biên bản.</summary>
public sealed class HandoverDocumentNotice
{
    public HandoverDocumentNoticeKind Kind { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>ACCEPT hoặc DECLINE.</summary>
    public string DocumentKind { get; set; } = string.Empty;

    public string DocumentCode { get; set; } = string.Empty;

    /// <summary>Họ tên người nhận để người nhận thư biết biên bản của ai; rỗng nếu chưa có.</summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Lý do trả về, chỉ dùng cho RecipientReturned.</summary>
    public string? Reason { get; set; }
}
