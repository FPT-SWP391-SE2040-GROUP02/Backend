using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một lượt bàn giao: một chỉ định (gói + người nhận) đi qua các bước nhận tài sản (bảng HandoverTurns), SRS §7.
/// </summary>
/// <remarks>
/// - Mỗi người nhận đi riêng, lượt này không phụ thuộc lượt khác (JOINT-01).
/// - Chỉ lưu MÃ BĂM của token trong link mời (INVITE-01); token gốc chỉ nằm trong email.
/// - ContainerName và các HandoverSnapshotItem được CHÉP lúc ghim, nên đổi tên gói hay tài sản sau đó không đổi bản ghim (DATA-01).
/// - RowVersion là concurrency token (giống @Version của Hibernate): hai thao tác cùng sửa một lượt thì bên lưu sau bị báo xung đột.
/// - DB chỉ cho một lượt đang hiệu lực (hoặc đã nhận) cho mỗi chỉ định (DATA-04) bằng chỉ mục unique có lọc.
/// </remarks>
public class HandoverTurn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Guid PlanId { get; set; }

    public Guid ContainerId { get; set; }

    /// <summary>Tên gói lúc ghim snapshot.</summary>
    public string ContainerName { get; set; } = string.Empty;

    public Guid DesignationId { get; set; }

    public Guid BeneficiaryId { get; set; }

    public HandoverTurnStatus Status { get; set; }

    /// <summary>SHA-256 chữ thường (hex, 64 ký tự) của token trong link mời.</summary>
    public string InvitationTokenHash { get; set; } = string.Empty;

    public DateTimeOffset InvitedAt { get; set; }

    /// <summary>Hạn phản hồi: InvitedAt + 5 ngày.</summary>
    public DateTimeOffset ResponseDeadline { get; set; }

    /// <summary>Chỉ đặt khi máy chủ email đã nhận thư mời; null nghĩa là chưa gửi được (INVITE-04).</summary>
    public DateTimeOffset? InvitationSentAt { get; set; }

    /// <summary>Số lần gửi lời mời tự động thất bại (OPS-04). Đủ 5 lần thì ngừng thử và mở sự cố NotificationFailed.</summary>
    public int InvitationAttempts { get; set; }

    /// <summary>Lần gửi lời mời gần nhất bị lỗi; dùng để giãn cách giữa các lần thử lại.</summary>
    public DateTimeOffset? InvitationLastAttemptAt { get; set; }

    /// <summary>Tài khoản đã nhận lượt qua link; null khi chưa ai nhận.</summary>
    public Guid? ClaimantUserId { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>
    /// true khi lượt bỏ qua bước xác minh danh tính nhờ công tắc cấu hình (chỉ Development).
    /// Dùng để nhìn ra trong DB và trên giao diện rằng lượt này CHƯA được xác minh thật.
    /// </summary>
    public bool IdentityBypassed { get; set; }

    /// <summary>Lúc lượt sẵn sàng để đồng ý/từ chối.</summary>
    public DateTimeOffset? ReadyAt { get; set; }

    /// <summary>Hạn quyết định: ReadyAt + 7 ngày.</summary>
    public DateTimeOffset? DecisionDeadline { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Lúc lượt kết thúc (đã nhận, từ chối hoặc hết hạn quyết định).</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>RETENTION-01..09: trạng thái lưu dữ liệu của lượt.</summary>
    public RetentionStatus RetentionStatus { get; set; }

    /// <summary>Lúc đồng hồ lưu bắt đầu lần đầu (retention_started_at); không đổi khi tạm dừng rồi chạy lại.</summary>
    public DateTimeOffset? RetentionStartedAt { get; set; }

    /// <summary>Hạn lưu hiện tại (retention_deadline) khi đang chạy; null khi chưa chạy hoặc đang tạm dừng.</summary>
    public DateTimeOffset? RetentionDeadline { get; set; }

    /// <summary>Số giây còn lại khi tạm dừng (RETENTION-04): chạy lại thì cộng phần này, không đặt lại 30 ngày.</summary>
    public long? RetentionRemainingSeconds { get; set; }

    /// <summary>Mốc nhắc đã gửi: 0 chưa, 1 lúc bắt đầu, 2 còn 7 ngày, 3 còn 24 giờ (RETENTION-05, §7.7 bước 5).</summary>
    public int RetentionNoticeStage { get; set; }

    /// <summary>Lúc dọn dữ liệu xong (tombstone); null khi chưa dọn.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public List<HandoverSnapshotItem> Items { get; set; } = new List<HandoverSnapshotItem>();

    public HandoverGrant? Grant { get; set; }
}
