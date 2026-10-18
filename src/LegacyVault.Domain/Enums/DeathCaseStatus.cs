namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái hồ sơ chứng tử (SRS §6.2 F2B, §11.1). Giá trị lưu trong DB là tên enum; không đổi tên giá trị đã có.
/// </summary>
/// <remarks>
/// DEATH-05, DATA-04: mỗi kho chỉ có một hồ sơ "đang mở" = mọi trạng thái trừ Rejected (kể cả Draft, quyết định D1).
/// </remarks>
public enum DeathCaseStatus
{
    /// <summary>Executor đang soạn bản đầu tiên, chưa gửi; kế hoạch chưa bị khóa.</summary>
    Draft = 0,
    /// <summary>Đã gửi; Verifier xét bản ReviewVersionNo (DEATH-01).</summary>
    UnderReview = 1,
    /// <summary>Verifier yêu cầu bổ sung; Executor tạo phiên bản mới (D5). Đặt ở nhánh B.</summary>
    SupplementRequired = 2,
    /// <summary>Verifier đã duyệt; kế hoạch chuyển sang bàn giao. Đặt ở nhánh B.</summary>
    ApprovedForHandover = 3,
    /// <summary>Bị từ chối; hồ sơ đóng, kho được mở hồ sơ mới. Đặt ở nhánh B.</summary>
    Rejected = 4,
    /// <summary>Tạm dừng (Owner báo còn sống / tranh chấp). Chỉ khai báo, F2C/F4 làm sau.</summary>
    Suspended = 5,
}
