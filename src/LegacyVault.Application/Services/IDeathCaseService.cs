using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Executor nộp hồ sơ chứng tử (F2B nhánh A, SRS §6.2). Người gọi lấy từ token, không nhận userId từ FE (SEC-04).
/// </summary>
/// <remarks>
/// Mọi thao tác yêu cầu người gọi là Executor đang hiệu lực (Accepted) của kho và không phải Owner;
/// không đủ điều kiện thì 404 để không lộ sự tồn tại của kho/hồ sơ.
/// </remarks>
public interface IDeathCaseService
{
    /// <summary>DMS-05: các kho mà tôi là Executor đang hiệu lực, kèm cờ quá hạn điểm danh.</summary>
    Task<List<ExecutorVaultResponse>> GetMyVaultsAsync(CancellationToken ct);

    /// <summary>
    /// DEATH-05: mở hồ sơ chứng tử (bản nháp) cho kho. Đã có hồ sơ đang mở thì trả lại hồ sơ đó (Created = false).
    /// Kế hoạch không ở trạng thái đang chạy → 409 DEATH_CASE_NOT_ALLOWED (PLAN_NOT_RUNNING).
    /// </summary>
    Task<OpenDeathCaseResult> OpenAsync(Guid vaultId, CancellationToken ct);

    Task<DeathCaseResponse> GetAsync(Guid caseId, CancellationToken ct);

    /// <summary>DEATH-01: sửa bản nháp hiện tại (chỉ bản chưa gửi), chống ghi đè bằng etag.</summary>
    Task<DeathCaseResponse> UpdateDraftAsync(Guid caseId, UpdateDeathCaseDraftRequest request, CancellationToken ct);

    /// <summary>
    /// DEATH-01, NFR-09: thêm một trang giấy chứng tử vào bản nháp (PDF/JPEG/PNG/WEBP theo nội dung thật,
    /// ≤ 20 MiB, tối đa 10 tệp). Loại sai → 400; quá lớn → 413; đủ 10 tệp → 409 DEATH_CASE_FILE_LIMIT_EXCEEDED.
    /// </summary>
    Task<DeathCaseResponse> AddFileAsync(Guid caseId, AddDeathCaseFileRequest request, CancellationToken ct);

    /// <summary>Gỡ một trang khỏi bản nháp. Bản đã gửi không gỡ được (VERSION_LOCKED).</summary>
    Task<DeathCaseResponse> RemoveFileAsync(Guid caseId, Guid fileId, CancellationToken ct);

    /// <summary>
    /// Executor hủy hồ sơ còn nháp (chưa gửi lần nào): xóa hồ sơ, các phiên bản và trang đã tải, kho trở lại như
    /// chưa báo tin (mở lại hồ sơ, điểm danh thay chủ kho được). Đã gửi → 409 DEATH_CASE_NOT_ALLOWED (VERSION_LOCKED),
    /// hồ sơ đã đóng → CASE_CLOSED.
    /// </summary>
    Task CancelDraftAsync(Guid caseId, CancellationToken ct);

    /// <summary>
    /// DEATH-01, DEATH-04, DEATH-05, DEATH-08, DEATH-10, DEATH-11, ASSIGN-01: Executor xác nhận và gửi bản hiện tại.
    /// Trong MỘT lần lưu: khóa bản, khóa kế hoạch (lần gửi đầu), đặt cờ, xếp thông báo Owner, phân công Verifier.
    /// Bản hiện tại đã gửi rồi thì trả trạng thái hiện tại (AC-71).
    /// </summary>
    Task<DeathCaseResponse> SubmitAsync(Guid caseId, SubmitDeathCaseRequest request, CancellationToken ct);

    /// <summary>
    /// DEATH-04, D5: tạo phiên bản bổ sung (chép dữ liệu và trang giấy của bản đã gửi) khi hồ sơ đang xét hoặc cần bổ sung.
    /// Chưa có bản đã gửi, hoặc đã có sẵn bản nháp → 409 DEATH_CASE_NOT_ALLOWED (CASE_NOT_REVISABLE).
    /// Verifier vẫn xét bản đã gửi gần nhất cho tới khi bản mới được gửi và xác nhận lại.
    /// </summary>
    Task<DeathCaseResponse> CreateVersionAsync(Guid caseId, CreateDeathCaseVersionRequest request, CancellationToken ct);
}
