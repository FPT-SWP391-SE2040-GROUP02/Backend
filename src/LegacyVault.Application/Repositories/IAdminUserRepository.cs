using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy vấn tài khoản cho Admin (ROLE-01). Danh sách chỉ đọc; không trả mật khẩu hay email đã xác minh chi tiết.</summary>
public interface IAdminUserRepository
{
    Task<(List<User> Items, int TotalCount)> ListAsync(
        string? search, UserRole? role, UserStatus? status, int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>Số Admin đang hoạt động (luôn phải còn ít nhất một).</summary>
    Task<int> CountActiveAdminsAsync(CancellationToken ct);

    /// <summary>Hồ sơ chứng tử đang chờ Verifier (đang xét hoặc cần bổ sung) mà chưa có phân công còn hiệu lực.</summary>
    Task<int> CountCasesWithoutVerifierAsync(CancellationToken ct);

    /// <summary>Hồ sơ đang xét hoặc cần bổ sung của Verifier (phân công còn hiệu lực), hạn gần nhất trước.</summary>
    Task<List<(Guid CaseId, DeathCaseStatus Status, DateTimeOffset DueAt)>> ListOpenAssignmentsAsync(
        Guid verifierUserId, CancellationToken ct);

    /// <summary>Các quyết định gần nhất của Verifier, mới nhất trước.</summary>
    Task<List<DeathCaseDecision>> ListRecentDecisionsAsync(Guid verifierUserId, int take, CancellationToken ct);

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
