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

    /// <summary>
    /// Thêm phiên bản mới của tài sản rồi lưu ngay (cùng các thay đổi đang chờ, ví dụ CurrentVersionNumber).
    /// Trả false khi DB báo trùng (AssetId, VersionNumber): request khác vừa tạo phiên bản này.
    /// Khi trả false, mọi thay đổi chưa lưu đã bị bỏ theo dõi.
    /// </summary>
    Task<bool> TryAddVersionAsync(AssetVersion version, CancellationToken ct);

    /// <summary>true khi tài sản đã được ghim vào snapshot của một lượt bàn giao (không được xóa).</summary>
    Task<bool> IsPinnedInHandoverAsync(Guid assetId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ DELETE" tài sản cùng các phiên bản đã nạp.</summary>
    void Remove(Asset asset);
}
