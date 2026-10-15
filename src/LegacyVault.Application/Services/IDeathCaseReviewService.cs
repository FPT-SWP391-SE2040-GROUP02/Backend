using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Verifier xét hồ sơ chứng tử (F2B nhánh B, SRS §6.2). Người gọi lấy từ token, không nhận userId từ FE.
/// </summary>
/// <remarks>
/// Mọi hàm bắt đầu bằng kiểm tra tài khoản Verifier đang hoạt động đọc từ DB (không tin claim role, ROLE-04):
/// không có tài khoản → 401 SESSION_EXPIRED; không Active → 403 ACCOUNT_NOT_ACTIVE; không phải Verifier → 403 ROLE_NOT_ALLOWED.
/// Sau đó hồ sơ không tồn tại hoặc tôi không có phân công ĐANG HIỆU LỰC → 404 DEATH_CASE_NOT_FOUND (SEC-04, SEC-05).
/// </remarks>
public interface IDeathCaseReviewService
{
    /// <summary>
    /// SEC-05: các hồ sơ đang giao cho tôi, một trang. sortBy "dueAt" (mặc định) hoặc "assignedAt";
    /// giá trị lạ → 400. searchTerm được bỏ qua. Hồ sơ còn mở đứng trước hồ sơ đã đóng.
    /// </summary>
    Task<PaginatedList<VerifierDeathCaseSummaryResponse>> ListAsync(PaginationRequest pagination, CancellationToken ct);

    /// <summary>
    /// Chi tiết hồ sơ: bản đang xét giải mã đầy đủ (DATA-02 cách hiểu ở mục 6.12), bản cũ chỉ metadata,
    /// kèm cờ, quyết định, lý do chưa duyệt được.
    /// </summary>
    Task<VerifierDeathCaseResponse> GetAsync(Guid caseId, CancellationToken ct);

    /// <summary>
    /// DATA-06: chuẩn bị một trang giấy của bản ĐÃ GỬI để xem. Kiểm toàn vẹn (lượt 1) trước khi trả;
    /// hỏng → ContentIntegrityException và chưa có byte nội dung nào được gửi.
    /// </summary>
    Task<PreparedDeathCaseFile> PrepareFileAsync(Guid caseId, Guid fileId, CancellationToken ct);

    /// <summary>
    /// DEATH-10: xem xét một cờ của bản đang xét (ghi ghi chú). Gọi lặp giữ ghi chú đầu. Thao tác làm đổi etag hồ sơ.
    /// Lỗi: 404 (không thấy cờ), 409 CASE_CLOSED / EDIT_CONFLICT / NOT_UNDER_REVIEW / VERIFIER_CONFLICT.
    /// </summary>
    Task<VerifierDeathCaseResponse> ReviewFlagAsync(
        Guid caseId, Guid flagId, ReviewDeathCaseFlagRequest request, CancellationToken ct);

    /// <summary>
    /// DEATH-01: yêu cầu Executor bổ sung (hồ sơ → SUPPLEMENT_REQUIRED, giữ phân công và đồng hồ 72 giờ).
    /// Gọi lặp cùng nội dung trả trạng thái hiện tại. Làm được cả khi Executor đang có bản nháp.
    /// </summary>
    Task<VerifierDeathCaseResponse> RequestSupplementAsync(
        Guid caseId, RequestDeathCaseSupplementRequest request, CancellationToken ct);

    /// <summary>
    /// DEATH-12: từ chối hồ sơ (lý do bắt buộc). Hồ sơ → REJECTED, đóng; kế hoạch về trạng thái trước khi gửi;
    /// thông báo Owner đang chờ bị hủy. Gọi lặp trên hồ sơ đã bị từ chối trả 200; hồ sơ đã duyệt → 409 CASE_CLOSED.
    /// Kế hoạch không ở DEATH_CASE_OPEN hoặc hồ sơ thiếu trạng thái trước → 409 PLAN_STATE_INVALID.
    /// </summary>
    Task<VerifierDeathCaseResponse> RejectAsync(Guid caseId, RejectDeathCaseRequest request, CancellationToken ct);

    /// <summary>
    /// DEATH-06: duyệt hồ sơ và mở bàn giao F3 (IHandoverStarter) trong MỘT lần lưu. Chưa duyệt được thì 409
    /// DEATH_CASE_NOT_ALLOWED (reason = lý do đầu, approveBlockers = đủ danh sách) hoặc PLAN_STATE_INVALID;
    /// không có gói nào để bàn giao thì 409 HANDOVER_NOT_ALLOWED và hồ sơ vẫn đang xét.
    /// Gọi lặp trên hồ sơ đã duyệt trả 200 (handover = null); hồ sơ đã bị từ chối thì 409 CASE_CLOSED.
    /// </summary>
    Task<VerifierDeathCaseResponse> ApproveAsync(Guid caseId, ApproveDeathCaseRequest request, CancellationToken ct);
}
