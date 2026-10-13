namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một phiên bản nội dung của tài sản (bảng AssetVersions).
/// </summary>
/// <remarks>
/// - Phiên bản đã tạo không bị ghi đè: thay nội dung = tạo phiên bản mới (snapshot/grant có thể tham chiếu bản cũ).
/// - Nội dung không nằm trong DB; StorageKey trỏ tới object trong kho lưu trữ private (IAssetContentStore).
/// - QUOTA-01: SizeBytes tính vào dung lượng đã dùng của kho; gỡ khỏi gói không giải phóng (QUOTA-02).
/// - Nội dung được mã hóa envelope: DEK riêng mỗi phiên bản, DEK lưu ở đây dưới dạng ĐÃ BỌC bằng KEK;
///   KEK nằm trong cấu hình server, không nằm trong DB. Lộ DB + tệp mà không có KEK vẫn không đọc được nội dung.
/// </remarks>
public class AssetVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid AssetId { get; set; }

    public Asset Asset { get; set; } = null!;

    /// <summary>1, 2, 3... duy nhất trong một tài sản.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Tên tệp gốc (đã làm sạch) với tài sản File; null với tài sản Text.</summary>
    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    /// <summary>Kích thước nội dung gốc, tính bằng byte.</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 của nội dung gốc, dạng hex chữ thường; dùng kiểm tra toàn vẹn khi đọc lại.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Khóa object trong kho lưu trữ; chỉ backend dùng, không trả cho FE.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Id của KEK đã bọc WrappedDataKey (cho phép đổi KEK mà vẫn mở được phiên bản cũ).</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK của phiên bản này, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; }
}
