using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Tài sản số trong một kho (bảng Assets), SRS §5.2.
/// </summary>
/// <remarks>
/// - Tài sản thuộc KHO, không thuộc gói: Owner tạo tài sản trước, sau đó mới đưa vào Container (SETUP-03).
/// - Nội dung nằm ở AssetVersion; tài sản chỉ giữ metadata và số phiên bản hiện hành.
///   Container liên kết với tài sản, nội dung bàn giao là phiên bản hiện hành (SETUP-03).
/// - Tên/mô tả KHÔNG mã hóa: không ghi mật khẩu, seed phrase vào đây (ASSET-02).
/// </remarks>
public class Asset
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Vault Vault { get; set; } = null!;

    public AssetKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Số của phiên bản hiện hành (AssetVersion.VersionNumber). Bắt đầu từ 1.</summary>
    public int CurrentVersionNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; dùng làm ETag chống ghi đè khi sửa.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<AssetVersion> Versions { get; set; } = new List<AssetVersion>();
}
