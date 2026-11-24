using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một lượt bàn giao: một người nhận trong biên bản đi qua các bước nhận tài sản (bảng HandoverTurns).
/// </summary>
/// <remarks>
/// - Mỗi người nhận đi riêng, lượt này không phụ thuộc lượt khác.
/// - Chỉ lưu MÃ BĂM của token trong link mời; token gốc chỉ nằm trong email.
/// - ContainerName (tiêu đề lượt) và các HandoverSnapshotItem được CHÉP lúc ghim, nên đổi tên tài sản sau đó không đổi bản ghim.
/// - RowVersion là concurrency token (giống @Version của Hibernate): hai thao tác cùng sửa một lượt thì bên lưu sau bị báo xung đột.
/// </remarks>
public class HandoverTurn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Guid PlanId { get; set; }

    /// <summary>Biên bản bàn giao đã tạo lượt này; mỗi người nhận trong biên bản có một lượt.</summary>
    public Guid? RecordId { get; set; }

    /// <summary>Tiêu đề lượt lúc ghim snapshot (tên cột giữ lại từ thời còn gói tài sản).</summary>
    public string ContainerName { get; set; } = string.Empty;

    public Guid BeneficiaryId { get; set; }

    public HandoverTurnStatus Status { get; set; }

    /// <summary>SHA-256 chữ thường (hex, 64 ký tự) của token trong link mời.</summary>
    public string InvitationTokenHash { get; set; } = string.Empty;

    public DateTimeOffset InvitedAt { get; set; }

    /// <summary>Hạn phản hồi: InvitedAt + 5 ngày.</summary>
    public DateTimeOffset ResponseDeadline { get; set; }

    /// <summary>Chỉ đặt khi máy chủ email đã nhận thư mời; null nghĩa là chưa gửi được.</summary>
    public DateTimeOffset? InvitationSentAt { get; set; }

    /// <summary>Số lần gửi lời mời tự động thất bại. Đủ 5 lần thì ngừng thử và ghi log cảnh báo.</summary>
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

    /// <summary>
    /// Executor đã xác nhận đúng người bằng tay thay cho eKYC; null khi không có xác nhận này.
    /// Cùng với IdentityConfirmedAt để truy lại ai đã bảo lãnh người nhận khi có tranh chấp.
    /// </summary>
    public Guid? IdentityConfirmedByUserId { get; set; }

    public DateTimeOffset? IdentityConfirmedAt { get; set; }

    /// <summary>
    /// Lúc người nhận nhập đúng mã nhận do Người thực thi đưa (khóa do Chủ kho cấp, xem ExecutorKeyShard).
    /// Chỉ cần khi kho đã cấp khóa; kho chưa cấp khóa thì bỏ qua bước này.
    /// </summary>
    public DateTimeOffset? KeyVerifiedAt { get; set; }

    /// <summary>Số lần nhập sai mã nhận; đủ giới hạn thì khóa lượt này (cần Người thực thi hoặc Admin xử lý).</summary>
    public int KeyFailedCount { get; set; }

    /// <summary>Lúc lượt sẵn sàng để đồng ý/từ chối.</summary>
    public DateTimeOffset? ReadyAt { get; set; }

    /// <summary>Hạn quyết định: ReadyAt + 7 ngày.</summary>
    public DateTimeOffset? DecisionDeadline { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Lúc người nhận bấm "Tôi đã nhận đủ" sau khi tải tài sản; Người thực thi thấy để biết lượt xong.</summary>
    public DateTimeOffset? ReceivedAllAt { get; set; }

    /// <summary>Phiếu giao nhận: lúc Người thực thi ký bên giao, sau khi người nhận xác nhận đã nhận đủ.</summary>
    public DateTimeOffset? ReceiptExecutorSignedAt { get; set; }

    public Guid? ReceiptExecutorUserId { get; set; }

    /// <summary>Người nhận báo tài sản thiếu hoặc sai khi nhận; null khi không báo.</summary>
    public string? ReceiptIssueNote { get; set; }

    public DateTimeOffset? ReceiptIssueAt { get; set; }

    /// <summary>Lúc lượt kết thúc (đã nhận, từ chối hoặc hết hạn quyết định).</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Trạng thái lưu dữ liệu của lượt.</summary>
    public RetentionStatus RetentionStatus { get; set; }

    /// <summary>Lúc đồng hồ lưu bắt đầu lần đầu (retention_started_at); không đổi khi tạm dừng rồi chạy lại.</summary>
    public DateTimeOffset? RetentionStartedAt { get; set; }

    /// <summary>Hạn lưu hiện tại (retention_deadline) khi đang chạy; null khi chưa chạy hoặc đang tạm dừng.</summary>
    public DateTimeOffset? RetentionDeadline { get; set; }

    /// <summary>Số giây còn lại khi tạm dừng: chạy lại thì cộng phần này, không đặt lại 30 ngày.</summary>
    public long? RetentionRemainingSeconds { get; set; }

    /// <summary>Mốc nhắc đã gửi: 0 chưa, 1 lúc bắt đầu, 2 còn 7 ngày, 3 còn 24 giờ.</summary>
    public int RetentionNoticeStage { get; set; }

    /// <summary>Lúc dọn dữ liệu xong (tombstone); null khi chưa dọn.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public List<HandoverSnapshotItem> Items { get; set; } = new List<HandoverSnapshotItem>();

    public HandoverGrant? Grant { get; set; }
}
