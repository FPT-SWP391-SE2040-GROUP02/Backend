using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng EkycVerifications (kết quả xác minh danh tính).</summary>
public interface IEkycVerificationRepository
{
    /// <summary>Lượt eKYC mới nhất của người dùng (có theo dõi thay đổi). Chưa có → null.</summary>
    Task<EkycVerification?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Lượt eKYC mới nhất của người dùng cho đúng một lượt bàn giao (EkycVerification.HandoverTurnId), chỉ đọc.
    /// Chưa có → null. Dùng ở luồng nhận tài sản để biết người nhận đã xác minh cho lượt đó chưa.
    /// </summary>
    Task<EkycVerification?> FindLatestByHandoverTurnAsync(Guid userId, Guid handoverTurnId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" lượt eKYC mới.</summary>
    void Add(EkycVerification verification);
}
