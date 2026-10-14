using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng EkycVerifications (kết quả xác minh danh tính).</summary>
public interface IEkycVerificationRepository
{
    /// <summary>Lượt eKYC mới nhất của người dùng (có theo dõi thay đổi). Chưa có → null.</summary>
    Task<EkycVerification?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" lượt eKYC mới.</summary>
    void Add(EkycVerification verification);
}
