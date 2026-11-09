using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Đọc các gói bàn giao của kho kèm tài sản và người nhận, cho màn Tài sản của Người thực thi.</summary>
public interface IExecutorAssetReader
{
    /// <summary>
    /// Các gói Ready của kho (không có bản nháp hay gói đã xóa), kèm tài sản chưa bị xóa nội dung và người nhận
    /// đã chỉ định. Không theo dõi thay đổi. Sắp theo tên gói rồi Id.
    /// </summary>
    Task<List<Container>> FindReadyPackagesAsync(Guid vaultId, CancellationToken ct);
}
