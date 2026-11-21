using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Yêu cầu nhận tài sản của một người nhận (bảng HandoverClaims), kèm giấy tờ nộp cho luật sư xét.
/// </summary>
/// <remarks>
/// - Người nhận mở link riêng trong thư báo kích hoạt, đăng nhập đúng email Chủ kho đã khai rồi gửi yêu cầu.
/// - Yêu cầu đầu tiên của kho tạo biên bản bàn giao và phân công luật sư; các yêu cầu sau gắn vào biên bản đang mở.
/// - Mỗi người nhận chỉ có một yêu cầu trong một biên bản (unique RecordId + BeneficiaryId).
/// - Hệ thống chỉ lưu tệp, không kiểm từng ô giấy tờ; luật sư xác minh ở ngoài hệ thống.
/// </remarks>
public class HandoverClaim
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RecordId { get; set; }

    public HandoverRecord Record { get; set; } = null!;

    public Guid BeneficiaryId { get; set; }

    /// <summary>Tài khoản người nhận đã gửi yêu cầu.</summary>
    public Guid UserId { get; set; }

    public HandoverClaimStatus Status { get; set; }

    /// <summary>Lời nhắn của người nhận gửi luật sư; rỗng khi không ghi.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Lý do luật sư yêu cầu bổ sung hoặc từ chối; null khi chưa có.</summary>
    public string? ReviewReason { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; concurrency token.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
