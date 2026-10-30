namespace LegacyVault.Domain.Entities;

/// <summary>
/// Liên kết tài sản ↔ Container (bảng ContainerAssets), SETUP-03.
/// </summary>
/// <remarks>
/// - Mỗi tài sản thuộc tối đa MỘT Container chưa bị xóa. Container đã xóa không còn tài sản (SETUP-16),
///   nên giữ được bằng unique index trên AssetId (DATA-04: chặn ở DB, không chỉ ở code).
/// - Gỡ liên kết không giải phóng dung lượng của tài sản (QUOTA-02).
/// </remarks>
public class ContainerAsset
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ContainerId { get; set; }

    public Container Container { get; set; } = null!;

    public Guid AssetId { get; set; }

    public Asset Asset { get; set; } = null!;

    public DateTimeOffset AddedAt { get; set; }
}
