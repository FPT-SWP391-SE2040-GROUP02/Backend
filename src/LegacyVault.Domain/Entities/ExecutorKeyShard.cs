namespace LegacyVault.Domain.Entities;

/// <summary>
/// Mảnh khóa Chủ kho giao cho Người thực thi (bảng ExecutorKeyShards). Mỗi kho có tối đa một bản ghi, khóa chính là VaultId.
/// </summary>
/// <remarks>
/// - Chỉ lưu ShardHash (SHA-256 có muối theo kho và phiên bản); mảnh gốc chỉ hiện một lần cho Chủ kho lúc cấp (SEC-07).
/// - "Quên khóa": Chủ kho cấp lại thì Version tăng, hash mới thay hash cũ nên mảnh cũ hết hiệu lực.
/// - Mảnh này là một trong hai mảnh của mã mở gói (mảnh kia nằm trong email gửi người nhận); việc ghép bằng HKDF
///   làm ở luồng mở gói của người nhận, bảng này chỉ giữ thông tin để kiểm mảnh Người thực thi đưa ra.
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
