using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một tài sản được ghim cho một lượt bàn giao (bảng HandoverSnapshotItems), SRS DATA-01.
/// </summary>
/// <remarks>
/// Các trường chép từ tài sản lúc ghim để đổi tên sau đó không ảnh hưởng; nội dung tải theo AssetId.
/// </remarks>
public class HandoverSnapshotItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TurnId { get; set; }

    public HandoverTurn Turn { get; set; } = null!;

    public Guid AssetId { get; set; }

    public string Name { get; set; } = string.Empty;

    public AssetKind Kind { get; set; }

    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Thứ tự hiển thị trong gói.</summary>
    public int Position { get; set; }
}
