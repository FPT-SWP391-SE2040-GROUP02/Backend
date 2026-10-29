using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Tài sản số trong một kho (bảng Assets), SRS §5.2.
/// </summary>
/// <remarks>
/// - Tài sản thuộc KHO, không thuộc gói: Owner tạo tài sản trước, sau đó mới đưa vào Container (SETUP-03).
/// - Mỗi tài sản có đúng một nội dung, không đổi sau khi tạo (muốn thay thì tạo tài sản mới và xóa tài sản cũ).
///   Container liên kết với tài sản, nội dung bàn giao là nội dung này (SETUP-03).
/// - Nội dung không nằm trong DB; StorageKey trỏ tới object trong kho lưu trữ private (IAssetContentStore).
/// - QUOTA-01: SizeBytes tính vào dung lượng đã dùng của kho; gỡ khỏi gói không giải phóng (QUOTA-02).
/// - Nội dung được mã hóa envelope: DEK riêng mỗi tài sản, DEK lưu ở đây dưới dạng ĐÃ BỌC bằng KEK;
///   KEK nằm trong cấu hình server, không nằm trong DB. Lộ DB + tệp mà không có KEK vẫn không đọc được nội dung.
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

    /// <summary>Tên tệp gốc (đã làm sạch) với tài sản File; null với tài sản Text.</summary>
    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    /// <summary>Kích thước nội dung gốc, tính bằng byte.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Khóa object trong kho lưu trữ; chỉ backend dùng, không trả cho FE.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Id của KEK đã bọc WrappedDataKey (cho phép đổi KEK mà vẫn mở được nội dung cũ).</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK của tài sản, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Tombstone (RETENTION-06): lúc khóa nội dung bị hủy. Khi đặt, WrappedDataKey đã bị ghi đè bằng 0 nên nội dung không
    /// giải mã được nữa kể cả khi xóa tệp lỗi. Null = nội dung còn.
    /// </summary>
    public DateTimeOffset? ContentDeletedAt { get; set; }

    /// <summary>Lúc tệp mã hóa đã xóa khỏi kho lưu trữ; null khi chưa xóa được (job thử lại).</summary>
    public DateTimeOffset? StorageDeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; dùng làm ETag chống ghi đè khi sửa.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
