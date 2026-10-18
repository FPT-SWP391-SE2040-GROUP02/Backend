namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một trang giấy chứng tử gắn với một phiên bản hồ sơ (bảng DeathCaseFiles).
/// </summary>
/// <remarks>
/// - NFR-09: loại tệp xác định theo nội dung thật (PDF, JPEG, PNG, WEBP), ≤ 20 MiB, ≤ 10 tệp mỗi phiên bản.
/// - D8: nội dung lưu qua IAssetContentStore, mã hóa envelope như tài sản, KHÔNG tính vào dung lượng kho.
/// - D8: tạo phiên bản mới thì chép dòng file và dùng chung StorageKey (CopiedFromFileId trỏ về dòng gốc);
///   nội dung chỉ bị xóa khi không còn dòng nào dùng StorageKey đó.
/// </remarks>
public class DeathCaseFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VersionId { get; set; }

    /// <summary>Thứ tự trang trong phiên bản, bắt đầu từ 1.</summary>
    public int Position { get; set; }

    /// <summary>Tên tệp gốc đã làm sạch (SEC-12: bỏ phần đường dẫn, ký tự điều khiển).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Loại tệp phát hiện từ nội dung thật, không lấy theo đuôi tệp (NFR-09).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Kích thước nội dung gốc, tính bằng byte.</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 của nội dung gốc, dạng hex chữ thường.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Khóa object trong kho lưu trữ; có thể dùng chung giữa các phiên bản (D8). Không trả cho FE.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Id của KEK đã bọc WrappedDataKey.</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK của nội dung tệp, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    public Guid UploadedByUserId { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>D8: dòng file của phiên bản trước mà dòng này được chép từ; null khi tải lên mới.</summary>
    public Guid? CopiedFromFileId { get; set; }
}
