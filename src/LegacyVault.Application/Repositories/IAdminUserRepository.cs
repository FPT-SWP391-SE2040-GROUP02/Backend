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

    /// <summary>Các tài khoản theo danh sách Id (không theo dõi thay đổi); Id không có thì bỏ qua.</summary>
    Task<List<User>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
