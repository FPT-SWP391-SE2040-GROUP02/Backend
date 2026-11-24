using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>
/// Truy cập dữ liệu phần gán tài sản cho người nhận (AssetAssignment). Chỉ đọc và ghi dữ liệu,
/// luật nghiệp vụ nằm ở AssetAssignmentService.
/// </summary>
public interface IAssetAssignmentRepository
{
    /// <summary>
    /// Mọi dòng gán của kho, đang được theo dõi để service thêm hoặc bớt. Sắp theo thời điểm gán rồi theo Id.
    /// </summary>
    Task<List<AssetAssignment>> FindByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Mọi dòng gán của kho kèm tài sản và người nhận, chỉ để đọc. Dùng cho biên bản bàn giao, thông báo
    /// kích hoạt, màn của Người thực thi và bản chụp di chúc.
    /// </summary>
    Task<List<AssetAssignment>> FindDetailedByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Số dòng gán của kho; lớn hơn 0 là điều kiện để kích hoạt kế hoạch.</summary>
    Task<int> CountByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Số tài sản khác nhau của kho đã được gán cho ít nhất một người nhận.</summary>
    Task<int> CountAssignedAssetsAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Tài sản có đang được gán cho ai không (chặn xóa tài sản).</summary>
    Task<bool> IsAssetAssignedAsync(Guid assetId, CancellationToken ct);

    /// <summary>Id các tài sản được gán cho từng người nhận trong danh sách. Người chưa được gán không có mặt trong kết quả.</summary>
    Task<Dictionary<Guid, List<Guid>>> FindAssetIdsByBeneficiaryIdsAsync(List<Guid> beneficiaryIds, CancellationToken ct);

    void Add(AssetAssignment assignment);

    void Remove(AssetAssignment assignment);

    /// <summary>
    /// Lưu mọi thay đổi đang chờ. Trả false khi hai yêu cầu cùng gán một cặp tài sản - người nhận và cơ sở dữ liệu
    /// chặn bằng chỉ mục duy nhất; khi đó mọi thay đổi chưa lưu của yêu cầu bị bỏ.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
