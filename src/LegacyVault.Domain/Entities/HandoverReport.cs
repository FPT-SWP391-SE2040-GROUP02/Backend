using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Hồ sơ báo tin Chủ kho đã mất (bảng HandoverReports), do người thân nộp kèm giấy chứng tử và di chúc.
/// </summary>
/// <remarks>
/// - Người nộp không cần được Chủ kho khai từ trước; nộp hồ sơ không làm họ thành người nhận.
/// - Kho được tìm bằng số căn cước người mất. Người nộp không biết có tìm thấy hay không (VaultId chỉ luật sư dùng).
/// - Nộp hồ sơ chưa khóa kho. Luật sư chấp nhận thì mới tạo (hoặc gắn vào) biên bản bàn giao và khóa kho.
/// - Thông tin người mất và người nhận gợi ý nằm trong DataProtected (mã hóa envelope).
/// </remarks>
public class HandoverReport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SubmitterUserId { get; set; }

    /// <summary>Luật sư được phân công xét hồ sơ.</summary>
    public Guid VerifierUserId { get; set; }

    /// <summary>Kho tìm thấy theo số căn cước người mất; null khi không có.</summary>
    public Guid? VaultId { get; set; }

    /// <summary>Biên bản hồ sơ được gắn vào khi luật sư chấp nhận; null khi chưa.</summary>
    public Guid? RecordId { get; set; }

    public HandoverClaimStatus Status { get; set; }

    public string Note { get; set; } = string.Empty;

    public string? ReviewReason { get; set; }

    /// <summary>Lý do luật sư vẫn chấp nhận dù có cảnh báo; chỉ luật sư xem. Null khi không có cảnh báo.</summary>
    public string? AcceptNote { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Đã mã hóa: họ tên, số căn cước, ngày mất của người mất và danh sách người nhận người nộp khai.</summary>
    public string DataProtected { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; concurrency token.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<HandoverReportFile> Files { get; set; } = new List<HandoverReportFile>();
}
