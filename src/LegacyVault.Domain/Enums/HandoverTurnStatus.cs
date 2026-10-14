namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái một lượt bàn giao (SRS §11.1). Giá trị lưu trong DB là tên enum; không đổi tên giá trị đã có.
/// </summary>
/// <remarks>
/// Kết thúc (terminal): HandoverCommitted, Declined, DecisionExpired, Cancelled, Lapsed.
/// Còn lại là "đang hiệu lực", kể cả InitialResponseExpired vì link vẫn dùng được (SRS §7.1).
/// </remarks>
public enum HandoverTurnStatus
{
    /// <summary>Đã gửi lời mời, chờ người nhận phản hồi (hạn 5 ngày).</summary>
    Invited = 0,
    /// <summary>Quá 5 ngày chưa phản hồi; chưa coi là từ chối, link vẫn dùng được.</summary>
    InitialResponseExpired = 1,
    /// <summary>Người nhận đã nhận lượt, đang chờ xác minh danh tính.</summary>
    IdentityInProgress = 2,
    /// <summary>Danh tính đã được xác nhận; người nhận được đồng ý hoặc từ chối trong 7 ngày.</summary>
    ReadyToAccept = 3,
    /// <summary>Đã đồng ý nhận và đã có grant.</summary>
    HandoverCommitted = 4,
    /// <summary>Người nhận từ chối.</summary>
    Declined = 5,
    /// <summary>Quá 7 ngày chưa quyết định.</summary>
    DecisionExpired = 6,
    /// <summary>Bị hủy (ví dụ Owner còn sống). Chưa có chức năng đặt trạng thái này.</summary>
    Cancelled = 7,
    /// <summary>Job dọn dữ liệu đặt khi nội dung đã bị xóa. Chưa có chức năng đặt trạng thái này.</summary>
    Lapsed = 8,
}
