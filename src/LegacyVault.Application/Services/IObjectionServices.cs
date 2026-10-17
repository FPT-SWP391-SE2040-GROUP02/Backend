using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Owner xem hồ sơ chứng tử của kho mình và báo "Tôi còn sống" (F2C, ALIVE-01..04, ALIVE-09).
/// Lỗi chung: 401, 404 VAULT_NOT_FOUND, 404 DEATH_CASE_NOT_FOUND (chưa có hồ sơ đã gửi).
/// </summary>
public interface IOwnerObjectionService
{
    Task<OwnerDeathCaseResponse> GetAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Báo còn sống: chặn ngay hồ sơ, kế hoạch và grant. Báo lặp chỉ tăng bộ đếm, không đổi hạn (ALIVE-09).
    /// Lỗi: 409 DEATH_CASE_NOT_ALLOWED (OBJECTION_ALREADY_REJECTED), 409 EDIT_CONFLICT.
    /// </summary>
    Task<OwnerDeathCaseResponse> ReportAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Gửi selfie trực tiếp để so với mẫu khuôn mặt. Lỗi: 400/413 ảnh, 409 DEATH_CASE_NOT_ALLOWED
    /// (OBJECTION_WINDOW_CLOSED / SELFIE_ATTEMPTS_EXCEEDED), 503 EKYC_SERVICE_UNAVAILABLE (chưa có mẫu khuôn mặt).
    /// </summary>
    Task<OwnerDeathCaseResponse> SubmitSelfieAsync(
        Guid vaultId, Stream content, string fileName, string contentType, CancellationToken ct);
}

/// <summary>
/// Verifier xem bằng chứng và kết luận báo còn sống (ALIVE-05). Người gọi phải là Verifier đang hoạt động
/// được giao hồ sơ, không phải Owner/Executor của kho; không đủ điều kiện thì 404 DEATH_CASE_NOT_FOUND.
/// </summary>
public interface IVerifierObjectionService
{
    /// <summary>Báo còn sống gần nhất của hồ sơ; chưa có → 404.</summary>
    Task<ObjectionResponse> GetAsync(Guid caseId, CancellationToken ct);

    /// <summary>
    /// Kết luận. Lỗi: 400 (kết luận lạ, thiếu lý do), 409 DEATH_CASE_NOT_ALLOWED (OBJECTION_NOT_OPEN /
    /// ALIVE_EVIDENCE_REQUIRED), 409 EDIT_CONFLICT. Gọi lặp trên báo đã kết luận trả trạng thái hiện tại.
    /// </summary>
    Task<ObjectionResponse> ConcludeAsync(Guid caseId, ConcludeObjectionRequest request, CancellationToken ct);
}
