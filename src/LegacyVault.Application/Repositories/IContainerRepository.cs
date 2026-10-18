using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Containers (kèm ContainerAssets và Designations).</summary>
public interface IContainerRepository
{
    /// <summary>
    /// Một Container trong ĐÚNG kho, kèm Assets và Designations (sắp theo thời điểm gắn), có theo dõi thay đổi.
    /// Không thấy / kho khác → null (SEC-04).
    /// </summary>
    Task<Container?> FindByIdAsync(Guid vaultId, Guid containerId, CancellationToken ct);

    /// <summary>SETUP-01: gói chưa lưu trữ kèm Assets và Designations, sắp theo CreatedAt rồi Id, chỉ đọc.</summary>
    Task<List<Container>> FindOpenByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Một trang Container của kho kèm Assets và Designations (sắp theo thời điểm gắn), kèm tổng số bản ghi khớp bộ lọc.
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
    /// Thêm chỉ định người nhận rồi lưu ngay (cùng các thay đổi đang chờ, ví dụ Plan.ConfigVersion).
    /// Trả false khi DB báo trùng (ContainerId, BeneficiaryId): request khác vừa gắn đúng người này vào đúng gói này.
    /// </summary>
    Task<bool> TryAddDesignationAsync(Designation designation, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ DELETE" một chỉ định người nhận.</summary>
    void RemoveDesignation(Designation designation);

    /// <summary>
    /// QUOTA-03: số người nhận KHÁC NHAU (theo BeneficiaryId) có ít nhất một chỉ định trong các gói của kho.
    /// Một người ở nhiều gói tính một suất. Gói lưu trữ không còn chỉ định nên không cần lọc theo trạng thái.
    /// </summary>
    Task<int> CountUsedBeneficiarySlotsAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Hồ sơ có đang được chỉ định ở gói nào của kho không.</summary>
    Task<bool> IsBeneficiaryDesignatedAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct);

    /// <summary>
    /// beneficiaryId → các containerId đang chỉ định hồ sơ đó (sắp theo thời điểm gắn), một truy vấn cho cả danh sách.
    /// Hồ sơ không có chỉ định nào thì KHÔNG có key trong kết quả.
    /// </summary>
    Task<Dictionary<Guid, List<Guid>>> FindContainerIdsByBeneficiaryIdsAsync(List<Guid> beneficiaryIds, CancellationToken ct);

    /// <summary>
    /// Bỏ MỌI thay đổi chưa lưu của cả request (ChangeTracker.Clear): mọi entity đang theo dõi
    /// (Container, Plan, Vault...) bị bỏ theo dõi, lần đọc sau lấy dữ liệu mới từ DB.
    /// Chỉ được gọi khi use case sẽ trả kết quả (hoặc ném lỗi) mà KHÔNG SaveChanges nữa.
    /// </summary>
    void DiscardPendingChanges();
}
