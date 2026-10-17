using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Assets (kèm AssetVersions).</summary>
public interface IAssetRepository
{
    /// <summary>
    /// Một tài sản trong ĐÚNG kho, kèm Versions (có theo dõi thay đổi). Không thấy / kho khác → null (SEC-04).
    /// Module gói dùng hàm này để kiểm tra assetId trước khi đưa vào Container.
    /// </summary>
    Task<Asset?> FindByIdAsync(Guid vaultId, Guid assetId, CancellationToken ct);

    /// <summary>ASSET-03, AC-68: tài sản chưa thuộc gói nào, sắp theo CreatedAt rồi Id, không nạp Versions.</summary>
    Task<List<Asset>> FindOutsideContainerByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Một trang tài sản của kho kèm Versions, kèm tổng số bản ghi khớp bộ lọc.
    /// searchTerm lọc theo tên; sortBy chỉ nhận giá trị service đã kiểm tra ("name" / "createdAt").
    /// pageIndex bắt đầu từ 1.
    /// </summary>
    Task<(List<Asset> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, int pageIndex, int pageSize, string? searchTerm,
        string sortBy, bool isAscending, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" tài sản mới (kèm Versions).</summary>
    void Add(Asset asset);
}
