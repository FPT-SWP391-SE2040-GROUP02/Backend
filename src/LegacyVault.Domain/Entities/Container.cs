using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Container (gói bàn giao) trong một kho (bảng Containers), SRS §5.2 bước 2 và 6.
/// </summary>
/// <remarks>
/// - SETUP-03: liên kết gắn với TÀI SẢN, không với phiên bản; nội dung gói là phiên bản hiện hành của tài sản.
/// - SETUP-04: Container không có phiên bản riêng; mọi thay đổi tăng HandoverPlan.ConfigVersion.
/// - SETUP-17: hạng Free chỉ được tạo/đổi tên/lưu trữ Container rỗng.
/// - NFR-14: tối đa 50 Container chưa lưu trữ mỗi kho.
/// Chỉ định người nhận (Designation) thêm khi làm module người nhận; tới lúc đó Container luôn là Draft.
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

    /// <summary>Thời điểm lưu trữ; null khi chưa lưu trữ.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    public List<ContainerAsset> Assets { get; set; } = new List<ContainerAsset>();
}
