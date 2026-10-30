using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Container (gói bàn giao) trong một kho (bảng Containers), SRS §5.2 bước 2 và 6.
/// </summary>
/// <remarks>
/// - SETUP-03: liên kết gắn với TÀI SẢN, không với phiên bản; nội dung gói là phiên bản hiện hành của tài sản.
/// - SETUP-04: Container không có phiên bản riêng; mọi thay đổi tăng HandoverPlan.ConfigVersion.
/// - SETUP-17: hạng Free chỉ được tạo/đổi tên/xóa Container rỗng.
/// - NFR-14: tối đa 50 Container chưa bị xóa mỗi kho.
/// - SETUP-16: READY khi có ≥1 tài sản VÀ ≥1 chỉ định người nhận (Designation); backend tự tính lại sau mỗi thay đổi.
/// </remarks>
public class Container
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Vault Vault { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Không mã hóa: không ghi mật khẩu, seed phrase vào đây (ASSET-02).</summary>
    public string? Description { get; set; }

    public ContainerStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Thời điểm xóa gói; null khi gói chưa bị xóa.</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    public List<ContainerAsset> Assets { get; set; } = new List<ContainerAsset>();

    /// <summary>Các chỉ định người nhận của gói, sắp theo thời điểm gắn.</summary>
    public List<Designation> Designations { get; set; } = new List<Designation>();
}
