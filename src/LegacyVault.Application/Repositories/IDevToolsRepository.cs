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
}
