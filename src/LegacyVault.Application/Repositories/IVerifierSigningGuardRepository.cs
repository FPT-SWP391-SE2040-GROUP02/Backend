using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>
/// DEATH-03: đọc/ghi bộ đếm nhập sai mật khẩu xác nhận duyệt/từ chối của Verifier.
/// AddGuard chỉ đưa vào bộ theo dõi; người gọi lưu cùng lần lưu của use case.
/// </summary>
public interface IVerifierSigningGuardRepository
{
    /// <summary>Bộ đếm nhập sai mật khẩu xác nhận của Verifier (có theo dõi để sửa); null nếu chưa sai lần nào.</summary>
    Task<VerifierSigningGuard?> FindGuardAsync(Guid userId, CancellationToken ct);

    void AddGuard(VerifierSigningGuard guard);

    /// <summary>Lỗi lưu do trùng khóa chính / chỉ mục unique (hai request cùng tạo dòng đầu tiên).</summary>
    bool IsUniqueViolation(Exception exception);
}
