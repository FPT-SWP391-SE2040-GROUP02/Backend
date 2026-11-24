using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy vấn tài khoản cho Admin (ROLE-01). Danh sách chỉ đọc; không trả mật khẩu hay email đã xác minh chi tiết.</summary>
public interface IAdminUserRepository
{
    Task<(List<User> Items, int TotalCount)> ListAsync(
        string? search, UserRole? role, UserStatus? status, int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>Số tài khoản theo vai trò, cùng bộ lọc tìm kiếm và trạng thái với danh sách nhưng bỏ qua vai trò.</summary>
    Task<Dictionary<UserRole, int>> CountByRoleAsync(string? search, UserStatus? status, CancellationToken ct);

    /// <summary>Số Admin đang hoạt động (luôn phải còn ít nhất một).</summary>
    Task<int> CountActiveAdminsAsync(CancellationToken ct);

    /// <summary>
    /// Việc chưa có luật sư làm: biên bản chưa hoàn tất, chưa hủy và hồ sơ báo tin còn chờ xét mà luật sư được giao không
    /// còn hoạt động (cần Admin giao lại).
    /// </summary>
    Task<int> CountCasesWithoutVerifierAsync(CancellationToken ct);

    /// <summary>Biên bản bàn giao chưa hoàn tất, chưa hủy của một luật sư, nhận sớm nhất trước.</summary>
    Task<List<(Guid RecordId, HandoverRecordStatus Status, DateTimeOffset RequestedAt)>> ListOpenRecordsAsync(
        Guid verifierUserId, CancellationToken ct);

    /// <summary>Hồ sơ báo tin còn chờ xét (chờ xét hoặc chờ bổ sung) được giao cho một luật sư, nộp sớm nhất trước.</summary>
    Task<List<(Guid ReportId, HandoverClaimStatus Status, DateTimeOffset CreatedAt)>> ListPendingReportsAsync(
        Guid verifierUserId, CancellationToken ct);

    /// <summary>
    /// Lần xét yêu cầu nhận tài sản gần nhất của luật sư (đã chấp nhận, từ chối hoặc yêu cầu bổ sung), mới nhất trước.
    /// Trả Id biên bản, không trả lý do.
    /// </summary>
    Task<List<(Guid RecordId, HandoverClaimStatus Status, DateTimeOffset ReviewedAt)>> ListRecentClaimReviewsAsync(
        Guid verifierUserId, int take, CancellationToken ct);

    /// <summary>Lần xét hồ sơ báo tin gần nhất của luật sư, mới nhất trước. Không trả lý do.</summary>
    Task<List<(Guid ReportId, HandoverClaimStatus Status, DateTimeOffset ReviewedAt)>> ListRecentReportReviewsAsync(
        Guid verifierUserId, int take, CancellationToken ct);

    /// <summary>
    /// Các kho mà tài khoản đang là Người thực thi hiện tại (Executor Accepted), kèm họ tên Chủ kho, trạng thái kho, kế
    /// hoạch và bước biên bản đang mở (null khi chưa có). Người nhận việc gần nhất trước.
    /// </summary>
    Task<List<(Guid VaultId, string OwnerFullName, VaultStatus VaultStatus, PlanStatus PlanStatus, HandoverRecordStatus? RecordStatus)>>
        ListHeldVaultsAsync(Guid executorUserId, CancellationToken ct);

    /// <summary>Các dòng nhật ký gần nhất do tài khoản thực hiện, mới nhất trước.</summary>
    Task<List<AuditLog>> ListRecentAuditByActorAsync(Guid userId, int take, CancellationToken ct);

    /// <summary>Số kế hoạch theo từng trạng thái (chỉ trạng thái có kế hoạch).</summary>
    Task<List<(PlanStatus Status, int Count)>> CountPlansByStatusAsync(CancellationToken ct);

    /// <summary>Số hồ sơ đang xét hoặc cần bổ sung của từng Verifier có phân công còn hiệu lực, kèm số hồ sơ quá hạn so với now.</summary>
    Task<List<(Guid VerifierUserId, int Open, int Overdue)>> CountVerifierLoadAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Các dòng nhật ký mới nhất toàn hệ thống (kèm họ tên người thực hiện nếu còn tài khoản).</summary>
    Task<List<(AuditLog Log, string? ActorFullName)>> ListRecentAuditAsync(int take, CancellationToken ct);

    /// <summary>Các tài khoản theo danh sách Id (không theo dõi thay đổi); Id không có thì bỏ qua.</summary>
    Task<List<User>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
