using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Biên bản bàn giao của một kho (bảng HandoverRecords): căn cứ duy nhất để giao tài sản.
/// </summary>
/// <remarks>
/// - Hệ thống chỉ quản lý tài sản, không làm pháp lý: luật sư lập biên bản theo kết quả đã xử lý ở ngoài.
/// - Người thực thi bấm mở bàn giao thì tạo biên bản và phân công một luật sư; mỗi kho chỉ có một biên bản.
/// - Luật sư có thể lấy file phân chia của Chủ kho (các gói và người nhận đã khai) làm bản đầu.
/// - RevisionNo tăng mỗi lần luật sư mở lại biên bản; chữ ký của bản cũ không còn hiệu lực (ký lại từ đầu).
/// - UpdatedAt là concurrency token chống hai thao tác cùng sửa.
/// </remarks>
public class HandoverRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VaultId { get; set; }
    public Guid PlanId { get; set; }
    public HandoverRecordStatus Status { get; set; }

    /// <summary>Số lần chốt lại: bắt đầu từ 1, tăng mỗi lần luật sư mở lại biên bản đã chốt.</summary>
    public int RevisionNo { get; set; } = 1;

    /// <summary>Người thực thi đã bấm mở bàn giao.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>Luật sư được phân công lập và kiểm biên bản.</summary>
    public Guid VerifierUserId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Lần chốt biên bản gần nhất; null khi đang lập.</summary>
    public DateTimeOffset? FinalizedAt { get; set; }

    /// <summary>Lúc đủ chữ ký ba bên; null khi chưa xong.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; concurrency token.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<HandoverRecordLine> Lines { get; set; } = new List<HandoverRecordLine>();
    public List<HandoverRecordSignature> Signatures { get; set; } = new List<HandoverRecordSignature>();
}
