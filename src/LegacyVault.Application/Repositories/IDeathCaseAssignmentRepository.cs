namespace LegacyVault.Application.Repositories;

/// <summary>
/// Truy vấn dành cho job phân công Verifier (ASSIGN-04). Tách khỏi IDeathCaseRepository để không phải
/// sửa các bản giả đang dùng ở test.
/// </summary>
public interface IDeathCaseAssignmentRepository
{
    /// <summary>
    /// ASSIGN-04, D2, D3, D5: Id các hồ sơ job cần xét, tối đa maxCount, sắp theo FirstSubmittedAt rồi Id.
    /// Hồ sơ UnderReview hoặc SupplementRequired mà:
    /// (a) không có phân công đang hiệu lực; hoặc
    /// (b) Verifier đang giữ không còn Active hoặc không còn vai trò Verifier; hoặc
    /// (c) hồ sơ UnderReview và DueAt - 12 giờ &lt;= now và (RemindedDueAt null hoặc khác DueAt); hoặc
    /// (d) hồ sơ UnderReview và DueAt &lt;= now.
    /// Hạn chế đã biết: hồ sơ quá hạn mà không có người thay vẫn nằm trong kết quả mỗi vòng; nếu quá 50 hồ sơ
    /// kẹt cùng lúc thì hồ sơ khác có thể bị đẩy sang vòng sau. Chấp nhận cho MVP.
    /// </summary>
    Task<List<Guid>> FindCaseIdsForJobAsync(DateTimeOffset now, int maxCount, CancellationToken ct);
}
