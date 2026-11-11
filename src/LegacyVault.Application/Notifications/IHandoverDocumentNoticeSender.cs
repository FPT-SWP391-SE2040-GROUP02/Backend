namespace LegacyVault.Application.Notifications;

/// <summary>Biên bản vừa chuyển sang bước nào, để chọn nội dung thư báo cho đúng người.</summary>
public enum HandoverDocumentNoticeKind
{
    /// <summary>Gửi Người xét duyệt: người nhận đã ký, cần kiểm tra thủ công.</summary>
    VerifierTurn = 0,

    /// <summary>Gửi người nhận: Người xét duyệt trả về để bổ sung.</summary>
    RecipientReturned = 1,

    /// <summary>Gửi người nhận và Người thực thi: biên bản hoàn tất.</summary>
    Completed = 2,
}

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

/// <summary>Gửi thư báo khi biên bản chuyển bước. Cài đặt ở Infrastructure (email hoặc chỉ ghi log).</summary>
public interface IHandoverDocumentNoticeSender
{
    Task SendAsync(HandoverDocumentNotice notice, CancellationToken ct);
}
