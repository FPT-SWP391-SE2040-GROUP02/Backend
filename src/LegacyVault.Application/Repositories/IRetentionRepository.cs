using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Đọc/ghi cho job lưu và xóa dữ liệu sau bàn giao (SRS §7.7). Hàm Find trả entity có theo dõi thay đổi.</summary>
public interface IRetentionRepository
{
    /// <summary>Id các kế hoạch đang/đã bàn giao còn lượt chưa dọn, hoặc kho chưa đóng mà mọi lượt đã dọn.</summary>
    Task<List<Guid>> FindPlanIdsForRetentionAsync(int maxCount, CancellationToken ct);

    /// <summary>Mọi lượt của kế hoạch kèm Items và Grant.</summary>
    Task<List<HandoverTurn>> FindTurnsAsync(Guid planId, CancellationToken ct);

    /// <summary>Các tài sản theo Id (chỉ tài sản còn nội dung).</summary>
    Task<List<Asset>> FindLiveAssetsAsync(List<Guid> assetIds, CancellationToken ct);

    /// <summary>Mọi tài sản còn nội dung của kho (dùng khi đóng kho, SETUP-18).</summary>
    Task<List<Asset>> FindLiveAssetsOfVaultAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Tài sản đã hủy khóa nhưng tệp chưa xóa được khỏi kho lưu trữ (thử lại).</summary>
    Task<List<Asset>> FindPendingStorageDeletesAsync(Guid vaultId, CancellationToken ct);
}
