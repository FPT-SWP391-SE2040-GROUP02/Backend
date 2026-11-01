using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Một dòng danh sách hồ sơ của Verifier (SEC-05): cột phẳng, chỉ 4 số cuối CCCD (DATA-02).</summary>
public sealed class VerifierCaseRow
{
    public Guid Id { get; set; }

    public DeathCaseStatus Status { get; set; }

    public int? ReviewVersionNo { get; set; }

    /// <summary>4 số cuối CCCD Owner của bản đang xét.</summary>
    public string? OwnerCitizenIdLast4 { get; set; }

    public DateOnly? DateOfDeath { get; set; }

    /// <summary>Lúc Executor gửi bản đang xét.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset? ObjectionEndsAt { get; set; }

    /// <summary>DEATH-01: lúc job đánh dấu Executor bổ sung quá hạn 7 ngày; null = chưa quá hạn hoặc đã gửi lại.</summary>
    public DateTimeOffset? SupplementOverdueAt { get; set; }

    /// <summary>DEATH-10: số cờ của bản đang xét chưa được xem xét.</summary>
    public int UnreviewedFlagCount { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>DEATH-12: hồ sơ bị từ chối trước đó của cùng kho, để Verifier xem lịch sử.</summary>
public sealed class PreviousCaseRow
{
    public Guid Id { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Lý do của quyết định Rejected; null nếu không có dòng quyết định.</summary>
    public string? RejectReason { get; set; }
}

/// <summary>
/// Truy cập dữ liệu Verifier xét hồ sơ chứng tử (F2B nhánh B): bảng DeathCaseDecisions và các truy vấn đọc riêng.
/// </summary>
/// <remarks>
/// Tách khỏi IDeathCaseRepository để không phải sửa các lớp giả (fake) đang dùng interface đó.
/// Repository chỉ đánh dấu Add; service gọi IUnitOfWork.SaveChangesAsync (STATE-02).
/// </remarks>
public interface IDeathCaseReviewRepository
{
    /// <summary>
    /// SEC-05, ASSIGN-01: các hồ sơ có phân công ĐANG HIỆU LỰC (EndedAt null) của Verifier, cộng hồ sơ họ đã tự kết luận
    /// Owner còn sống (ALIVE_CONFIRMED, hồ sơ Rejected) để xem lại ở lịch sử, một trang.
    /// Hồ sơ còn mở (ClosedAt null) trước, rồi theo hạn xét (sortBy "dueAt") hoặc lúc được giao ("assignedAt"),
    /// rồi theo Id. sortBy đã được service kiểm tra. pageIndex bắt đầu từ 1.
    /// </summary>
    Task<(List<VerifierCaseRow> Items, int TotalCount)> FindAssignedPageAsync(
        Guid verifierUserId, string sortBy, bool isAscending, int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>
    /// DATA-06, DEATH-04: trang giấy theo Id, chỉ khi thuộc một phiên bản ĐÃ GỬI của đúng hồ sơ caseId
    /// (bản nháp hoặc hồ sơ khác → null). Một truy vấn, không theo dõi.
    /// </summary>
    Task<DeathCaseFile?> FindSubmittedFileAsync(Guid caseId, Guid fileId, CancellationToken ct);

    /// <summary>DEATH-12: thông tin hồ sơ bị từ chối trước đó. Không có → null.</summary>
    Task<PreviousCaseRow?> FindPreviousCaseAsync(Guid previousCaseId, CancellationToken ct);

    void AddDecision(DeathCaseDecision decision);
}
