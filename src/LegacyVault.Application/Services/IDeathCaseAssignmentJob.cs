namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền giao Verifier cho hồ sơ chứng tử chưa có người giữ và giao lại khi quá hạn hoặc Verifier mất quyền
/// (ASSIGN-02, ASSIGN-04, D5). Job không bao giờ duyệt, từ chối hay đổi trạng thái hồ sơ.
/// </summary>
public interface IDeathCaseAssignmentJob
{
    /// <summary>ASSIGN-04: Id các hồ sơ cần xét (tối đa 50 mỗi vòng quét).</summary>
    Task<List<Guid>> FindCaseIdsAsync(CancellationToken ct);

    /// <summary>
    /// Xét MỘT hồ sơ: đọc lại, kiểm lại trạng thái, làm tối đa một việc, SaveChanges tối đa một lần.
    /// Chạy lặp an toàn (TIME-02, TIME-05).
    /// </summary>
    Task<DeathCaseAssignmentOutcome> ProcessCaseAsync(Guid caseId, CancellationToken ct);
}
