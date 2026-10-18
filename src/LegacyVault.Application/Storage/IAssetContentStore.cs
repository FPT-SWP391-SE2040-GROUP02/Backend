namespace LegacyVault.Application.Storage;

/// <summary>
/// Kho lưu nội dung tài sản (private, không có link public). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// - Nội dung LUÔN được mã hóa trước khi ghi: mã hóa envelope AES-256-GCM, mỗi phiên bản một DEK riêng,
///   DEK được bọc bằng KEK nằm ngoài DB (xem Infrastructure/Storage/DataKeyProtector).
/// - Service không biết nội dung nằm ở đâu (đĩa local, R2/S3...) hay mã hóa thế nào;
///   chỉ lưu các trường của StoredContent vào AssetVersion.
/// - Không log nội dung, DEK hoặc StorageKey kèm thông tin người dùng.
/// </remarks>
public interface IAssetContentStore
{
    /// <summary>
    /// Mã hóa và ghi toàn bộ stream thành một object mới; tính kích thước và SHA-256 của nội dung gốc khi ghi.
    /// Nội dung gốc quá maxBytes thì hủy, xóa phần đã ghi và ném AppException FILE_TOO_LARGE.
    /// </summary>
    Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken ct);

    /// <summary>
    /// Giải mã object và ghi nội dung gốc ra destination; kiểm tra lại kích thước và SHA-256.
    /// Dùng cho tải xuống/bàn giao (chưa có endpoint). Nội dung bị sửa hoặc sai khóa → CryptographicException.
    /// </summary>
    Task CopyToAsync(StoredContent content, Stream destination, CancellationToken ct);

    /// <summary>Xóa object (bù trừ khi lưu DB thất bại). Không có object → bỏ qua.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

/// <summary>Thông tin một nội dung đã lưu; service chép nguyên các trường này vào AssetVersion.</summary>
public class StoredContent
{
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Kích thước nội dung gốc (trước khi mã hóa), tính vào quota.</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 hex chữ thường của nội dung gốc.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Id của KEK đã dùng để bọc DEK.</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK đã bọc bằng KEK. Không bao giờ trả cho FE.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();
}
