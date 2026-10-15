using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một dòng nhật ký hoạt động (bảng AuditLogs): ai, vai trò gì, làm gì, trên đối tượng nào, lúc nào, kết quả ra sao (SEC-07).
/// </summary>
/// <remarks>
/// - Chỉ thêm dòng, không sửa, không xóa qua app (ADMIN-06, DATA-07). Giữ ít nhất 365 ngày (TIME-06).
/// - Không ghi mật khẩu, OTP, token, số CCCD đầy đủ, ảnh, dữ liệu khuôn mặt, khóa hay nội dung file (SEC-07).
///   Reason chỉ chứa mô tả ngắn không nhạy cảm; tên tài sản và tên người nhận cũng không ghi vào đây.
/// - Owner xem được các dòng của kho mình (SEC-13).
/// </remarks>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Kho liên quan; null với sự kiện không gắn kho nào.</summary>
    public Guid? VaultId { get; set; }

    /// <summary>Tài khoản thực hiện; null khi job nền thực hiện (ActorRole = System).</summary>
    public Guid? ActorUserId { get; set; }

    public AuditActorRole ActorRole { get; set; }

    public AuditAction Action { get; set; }

    public AuditTargetType TargetType { get; set; }

    public Guid? TargetId { get; set; }

    /// <summary>STATE-03: phiên bản đối tượng sau thao tác (ví dụ config_version của kế hoạch), nếu có.</summary>
    public int? TargetVersion { get; set; }

    public AuditOutcome Outcome { get; set; }

    /// <summary>Lý do hoặc ghi chú ngắn (≤ 500 ký tự), không chứa dữ liệu nhạy cảm.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset At { get; set; }
}
