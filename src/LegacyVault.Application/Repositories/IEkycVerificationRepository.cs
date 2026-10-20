using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng EkycVerifications (kết quả xác minh danh tính).</summary>
public interface IEkycVerificationRepository
{
    /// <summary>Lượt eKYC mới nhất của người dùng (có theo dõi thay đổi). Chưa có → null.</summary>
    Task<EkycVerification?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Lượt mới nhất của người dùng theo đúng mục đích (EKYC-01). Không có → null.</summary>
    Task<EkycVerification?> FindLatestByUserAndPurposeAsync(Guid userId, EkycPurpose purpose, CancellationToken ct);

    /// <summary>
    /// Người dùng đã có ít nhất một lượt đạt (PASSED) theo đúng mục đích chưa (EKYC-01). Lượt chụp lại đang chờ
    /// hoặc thất bại sau đó không xóa kết quả đã đạt.
    /// </summary>
    Task<bool> HasPassedByUserAndPurposeAsync(Guid userId, EkycPurpose purpose, CancellationToken ct);

    /// <summary>
    /// Lượt eKYC mới nhất của người dùng cho đúng một lượt bàn giao (EkycVerification.HandoverTurnId), chỉ đọc.
    /// Chưa có → null. Dùng ở luồng nhận tài sản để biết người nhận đã xác minh cho lượt đó chưa.
    /// </summary>
    Task<EkycVerification?> FindLatestByHandoverTurnAsync(Guid userId, Guid handoverTurnId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" lượt eKYC mới.</summary>
    void Add(EkycVerification verification);
}
