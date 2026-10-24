using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Đọc dữ liệu cho công cụ thử nghiệm của Admin (chỉ có ở Development). Không chứa luật nghiệp vụ.</summary>
public interface IDevToolsRepository
{
    /// <summary>Các kho kèm Owner và kế hoạch, chỉ đọc, sắp theo ngày tạo. Không nạp nội dung tài sản.</summary>
    Task<List<Vault>> ListVaultsAsync(int maxCount, CancellationToken ct);

    /// <summary>Một kho kèm kế hoạch, có theo dõi thay đổi để cập nhật.</summary>
    Task<Vault?> FindVaultAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Kỳ điểm danh đang mở của kế hoạch, có theo dõi thay đổi.</summary>
    Task<CheckInPeriod?> FindOpenPeriodAsync(Guid planId, CancellationToken ct);

    /// <summary>Hồ sơ chứng tử mới nhất chưa đóng của kho, có theo dõi thay đổi.</summary>
    Task<DeathCase?> FindOpenDeathCaseAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Lời báo còn sống mới nhất của kho, có theo dõi thay đổi.</summary>
    Task<DeathCaseObjection?> FindLatestObjectionAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Thao tác đồng hồ gần nhất của kho trong nhật ký: tua hoặc về thời gian hiện tại; null khi chưa từng có.</summary>
    Task<AuditAction?> FindLatestClockActionAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Mọi lượt eKYC của tài khoản, có theo dõi thay đổi để xóa.</summary>
    Task<List<EkycVerification>> ListEkycVerificationsAsync(Guid userId, CancellationToken ct);

    /// <summary>Mọi lần đồng ý chụp ảnh của tài khoản, có theo dõi thay đổi để xóa.</summary>
    Task<List<EkycConsent>> ListEkycConsentsAsync(Guid userId, CancellationToken ct);

    /// <summary>Danh tính đã gắn của Owner, có theo dõi thay đổi; null khi chưa có.</summary>
    Task<OwnerIdentity?> FindOwnerIdentityAsync(Guid userId, CancellationToken ct);

    /// <summary>Mọi phiên bản mẫu khuôn mặt của Owner, có theo dõi thay đổi để xóa.</summary>
    Task<List<OwnerFaceTemplate>> ListOwnerFaceTemplatesAsync(Guid userId, CancellationToken ct);

    /// <summary>Một kho kèm kế hoạch và quyền dịch vụ, có theo dõi thay đổi để cập nhật.</summary>
    Task<Vault?> FindVaultWithEntitlementAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Các đơn thanh toán đang chờ của kho, có theo dõi thay đổi để hủy.</summary>
    Task<List<PaymentOrder>> ListPendingPaymentOrdersAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Đánh dấu xóa dữ liệu eKYC; lưu khi gọi IUnitOfWork.SaveChangesAsync.</summary>
    void RemoveEkycData(
        List<EkycVerification> verifications, List<EkycConsent> consents,
        OwnerIdentity? identity, List<OwnerFaceTemplate> faceTemplates);
}
