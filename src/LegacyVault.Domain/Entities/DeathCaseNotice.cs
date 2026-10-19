using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Thông báo cho Owner rằng có hồ sơ chứng tử (bảng DeathCaseNotices), theo mẫu outbox (quyết định D3).
/// </summary>
/// <remarks>
/// - DEATH-08: gửi hồ sơ chỉ tạo dòng Pending trong cùng SaveChanges; job gửi thư, SMTP nhận → Delivered,
///   72 giờ phản đối tính từ DeliveredAt. Lỗi thì thử lại, đủ 5 lần → Failed và mở sự cố F4 (DEATH-08).
/// - DEATH-11: đổi trọng yếu ở phiên bản mới thì tạo thông báo mới cho VersionNo đó.
/// - TIME-02: thông báo đã Delivered không gửi lại. UpdatedAt là concurrency token để hai job không cùng xử lý.
/// - Không lưu địa chỉ email: job đọc email Owner lúc gửi qua RecipientUserId.
/// </remarks>
public class DeathCaseNotice
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaseId { get; set; }

    /// <summary>Phiên bản hồ sơ mà thông báo này báo cho Owner.</summary>
    public int VersionNo { get; set; }

    public DeathCaseNoticeChannel Channel { get; set; }

    /// <summary>Owner của kho.</summary>
    public Guid RecipientUserId { get; set; }

    public DeathCaseNoticeStatus Status { get; set; }

    /// <summary>Số lần đã thử gửi (tối đa 5).</summary>
    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>DEATH-08: lúc SMTP nhận thư; mốc bắt đầu 72 giờ phản đối.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Concurrency token: hai job cùng xử lý một thông báo thì bên lưu sau bị xung đột.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
