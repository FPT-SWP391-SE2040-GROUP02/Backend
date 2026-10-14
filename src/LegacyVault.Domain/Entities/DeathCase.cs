using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Hồ sơ chứng tử của một kho do Executor mở (bảng DeathCases), SRS §6.2 F2B.
/// </summary>
/// <remarks>
/// - DEATH-05, DATA-04: mỗi kho chỉ có một hồ sơ đang mở (mọi trạng thái trừ Rejected, kể cả Draft — D1),
///   giữ bằng chỉ mục unique có lọc trong DB nên hai Executor gửi cùng lúc vẫn chỉ ra một hồ sơ (AC-71, AC-91).
/// - Nội dung khai báo nằm ở các DeathCaseVersion; bản đã gửi không bao giờ sửa (DEATH-04).
/// - Kế hoạch chỉ chuyển DeathCaseOpen khi Executor gửi lần đầu (D1); trạng thái cũ giữ ở StatusBeforeCase (D2).
/// - UpdatedAt là concurrency token và nguồn etag chống ghi đè.
/// </remarks>
public class DeathCase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Guid PlanId { get; set; }

    public DeathCaseStatus Status { get; set; }

    /// <summary>
    /// D2 (lệch §11.6): trạng thái kế hoạch ngay trước khi gửi lần đầu, để nhánh B trả lại khi từ chối.
    /// Null khi hồ sơ còn Draft (chưa khóa kế hoạch).
    /// </summary>
    public PlanStatus? StatusBeforeCase { get; set; }

    /// <summary>Số phiên bản mới nhất (bản nháp hoặc bản vừa gửi). Bắt đầu từ 1.</summary>
    public int CurrentVersionNo { get; set; }

    /// <summary>D5: Verifier luôn xét bản gửi gần nhất; null khi chưa gửi lần nào.</summary>
    public int? ReviewVersionNo { get; set; }

    /// <summary>DEATH-08, DEATH-11: bản đã báo Owner gần nhất; đổi trọng yếu so với bản này thì báo lại.</summary>
    public int? NotifiedVersionNo { get; set; }

    /// <summary>DEATH-08: mốc bắt đầu 72 giờ phản đối = lúc thông báo bản NotifiedVersionNo được SMTP nhận.</summary>
    public DateTimeOffset? ObjectionStartsAt { get; set; }

    /// <summary>DEATH-08: ObjectionStartsAt + 72 giờ; null khi đồng hồ chưa chạy.</summary>
    public DateTimeOffset? ObjectionEndsAt { get; set; }

    /// <summary>Hồ sơ bị từ chối gần nhất của cùng kho (nếu có), để Verifier xem lịch sử.</summary>
    public Guid? PreviousCaseId { get; set; }

    /// <summary>Executor đã mở hồ sơ.</summary>
    public Guid OpenedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Lúc gửi lần đầu (khóa kế hoạch, DEATH-12 phần mở); null khi còn Draft.</summary>
    public DateTimeOffset? FirstSubmittedAt { get; set; }

    /// <summary>Lúc hồ sơ kết thúc (duyệt/từ chối). Đặt ở nhánh B.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; concurrency token và etag. Null khi mới tạo → etag "0".</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<DeathCaseVersion> Versions { get; set; } = new List<DeathCaseVersion>();

    public List<DeathCaseFlag> Flags { get; set; } = new List<DeathCaseFlag>();

    public List<DeathCaseNotice> Notices { get; set; } = new List<DeathCaseNotice>();

    public List<DeathCaseAssignment> Assignments { get; set; } = new List<DeathCaseAssignment>();
}
