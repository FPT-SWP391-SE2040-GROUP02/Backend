namespace LegacyVault.Domain.Entities;

/// <summary>
/// Phân công Verifier xét một hồ sơ chứng tử (bảng DeathCaseAssignments), quyết định D4.
/// </summary>
/// <remarks>
/// - ASSIGN-01: xoay vòng Verifier Active có lần được giao gần nhất cũ nhất (chưa từng giao đứng trước), hòa theo Id.
/// - ROLE-03: loại Owner, Executor đang hiệu lực của kho, người gửi. Chưa loại được người nhận (chưa gắn tài khoản trước F3).
/// - Mỗi hồ sơ chỉ một phân công còn hiệu lực (EndedAt null), giữ bằng chỉ mục unique có lọc.
/// - Không có Verifier phù hợp thì không tạo dòng (hồ sơ để trống).
/// </remarks>
public class DeathCaseAssignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaseId { get; set; }

    public Guid VerifierUserId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Hạn xét bản đang chờ: lúc giao hoặc lúc Executor gửi bản mới gần nhất + ReviewDueHours (ASSIGN-04).</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Lúc phân công kết thúc; chỉ đặt khi giao lại người khác (nhánh C). Duyệt/từ chối không đặt. Null khi còn hiệu lực.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Lý do kết thúc dạng mã chữ ngắn (≤ 32 ký tự); chỉ đặt khi giao lại người khác (nhánh C).</summary>
    public string? EndReason { get; set; }
}
