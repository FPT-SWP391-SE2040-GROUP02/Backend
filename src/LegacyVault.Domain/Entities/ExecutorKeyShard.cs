namespace LegacyVault.Domain.Entities;

/// <summary>
/// Mã mở gói bàn giao của kho (bảng ExecutorKeyShards). Mỗi kho có tối đa một bản ghi, khóa chính là VaultId.
/// </summary>
/// <remarks>
/// - Mã do server tạo, gồm 16 ký tự Crockford base32 (80 bit), hiển thị dạng "XXXX-XXXX-XXXX-XXXX".
/// - DB giữ ShardHash (SHA-256 gắn với kho và phiên bản) để đối chiếu, và bản mã hóa phong bì (AES-256-GCM, DEK bọc bằng KEK)
///   để Người thực thi đọc lại khi bàn giao đã bắt đầu. Mã gốc không bao giờ lưu dạng rõ và không vào nhật ký (SEC-07).
/// - Mã chỉ là cổng mở khóa việc tải của người nhận (người nhận nhập mã trước khi tải), không dùng để mã hóa nội dung tài sản.
/// - Đổi mã (Chủ kho đổi, hoặc hệ thống tự đổi khi đổi Người thực thi) thì Version tăng, hash mới thay hash cũ nên mã cũ hết hiệu lực.
/// </remarks>
public class ExecutorKeyShard
{
    public Guid VaultId { get; set; }

    /// <summary>Phiên bản mảnh, bắt đầu từ 1, tăng mỗi lần cấp lại.</summary>
    public int Version { get; set; }

    /// <summary>SHA-256 dạng hex của "vaultId:version:mảnh đã chuẩn hóa".</summary>
    public string ShardHash { get; set; } = string.Empty;

    /// <summary>Lần cấp đầu tiên.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Lần cấp lại gần nhất (quên khóa); null khi chưa cấp lại lần nào.</summary>
    public DateTimeOffset? ReissuedAt { get; set; }

    /// <summary>
    /// Mảnh khóa được mã hóa bảo mật (Envelope Encryption) để gửi trực tiếp cho Người thực thi trên hệ thống.
    /// Giải mã bằng KEK của hệ thống khi Người thực thi truy cập.
    /// </summary>
    public string? EncryptedShard { get; set; }

    /// <summary>Định danh KEK dùng để bọc khóa mã hóa mảnh.</summary>
    public string? EncryptionKeyId { get; set; }

    /// <summary>DEK đã bọc (wrapped data key) dùng để mã hóa mảnh.</summary>
    public byte[]? WrappedDataKey { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
