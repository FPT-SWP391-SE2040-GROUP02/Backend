using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Containers (kèm ContainerAssets).</summary>
public interface IContainerRepository
{
    /// <summary>Một Container trong ĐÚNG kho, kèm Assets (có theo dõi thay đổi). Không thấy / kho khác → null (SEC-04).</summary>
    Task<Container?> FindByIdAsync(Guid vaultId, Guid containerId, CancellationToken ct);

    /// <summary>
    /// Một trang Container của kho kèm Assets, kèm tổng số bản ghi khớp bộ lọc.
    /// searchTerm lọc theo tên; sortBy chỉ nhận giá trị service đã kiểm tra ("name" / "createdAt").
    /// pageIndex bắt đầu từ 1.
    /// </summary>
    Task<(List<Container> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, bool includeArchived, int pageIndex, int pageSize, string? searchTerm,
        string sortBy, bool isAscending, CancellationToken ct);

    /// <summary>Số Container chưa lưu trữ (NFR-14: tối đa 50).</summary>
    Task<int> CountActiveByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Số Container READY (SETUP-16: kế hoạch đang chạy mà về 0 thì phải cảnh báo Owner).</summary>
    Task<int> CountReadyByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Liên kết hiện có của một tài sản. Chưa thuộc gói nào → null.</summary>
    Task<ContainerAsset?> FindLinkByAssetIdAsync(Guid assetId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" Container mới.</summary>
    void Add(Container container);

    /// <summary>
    /// Thêm liên kết tài sản rồi lưu ngay (cùng các thay đổi đang chờ, ví dụ Plan.ConfigVersion).
    /// Trả false khi DB báo trùng AssetId: request khác vừa đưa tài sản này vào gói khác.
    /// </summary>
    Task<bool> TryAddAssetLinkAsync(ContainerAsset link, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ DELETE" một liên kết tài sản.</summary>
    void RemoveAssetLink(ContainerAsset link);

    /// <summary>
    /// Bỏ MỌI thay đổi chưa lưu của cả request (ChangeTracker.Clear): mọi entity đang theo dõi
    /// (Container, Plan, Vault...) bị bỏ theo dõi, lần đọc sau lấy dữ liệu mới từ DB.
    /// Chỉ được gọi khi use case sẽ trả kết quả (hoặc ném lỗi) mà KHÔNG SaveChanges nữa.
    /// </summary>
    void DiscardPendingChanges();
}
