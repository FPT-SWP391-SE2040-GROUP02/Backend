using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Cờ cảnh báo trên một phiên bản hồ sơ chứng tử (bảng DeathCaseFlags).
/// </summary>
/// <remarks>
/// - DEATH-10: Owner điểm danh sau ngày mất được khai → cờ CheckinAfterDeath; chặn duyệt tới khi Verifier xem xét.
/// - Unique (VersionId, Kind): mỗi loại cờ chỉ một dòng mỗi phiên bản.
/// - Detail không chứa dữ liệu nhạy cảm (CCCD, họ tên).
/// </remarks>
public class DeathCaseFlag
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaseId { get; set; }

    public Guid VersionId { get; set; }

    public DeathCaseFlagKind Kind { get; set; }

    /// <summary>Mô tả ngắn cho Verifier (≤ 200 ký tự), không chứa dữ liệu nhạy cảm.</summary>
    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Verifier đã xem xét cờ; null khi chưa xem (DEATH-10 chặn duyệt). Đặt ở nhánh B.</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewNote { get; set; }
}
