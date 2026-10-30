using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>
/// DEATH-14: đọc/ghi khóa ký của Verifier và bộ đếm nhập sai mật khẩu ký.
/// Add/AddGuard chỉ đưa vào bộ theo dõi; người gọi lưu cùng lần lưu của use case.
/// </summary>
public interface IVerifierSigningKeyRepository
{
    /// <summary>Khóa đang dùng (RevokedAt null) của Verifier; null nếu chưa có.</summary>
    Task<VerifierSigningKey?> FindActiveByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Khóa theo Id (kể cả đã thu hồi), dùng để kiểm lại chữ ký cũ.</summary>
    Task<VerifierSigningKey?> FindByIdAsync(Guid id, CancellationToken ct);

    void Add(VerifierSigningKey key);

    /// <summary>Bộ đếm nhập sai mật khẩu ký của Verifier (có theo dõi để sửa); null nếu chưa sai lần nào.</summary>
    Task<VerifierSigningGuard?> FindGuardAsync(Guid userId, CancellationToken ct);

    void AddGuard(VerifierSigningGuard guard);
}
