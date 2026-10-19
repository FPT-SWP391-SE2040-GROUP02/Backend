namespace LegacyVault.Domain.Entities;

/// <summary>
/// Mẫu khuôn mặt của Owner (bảng OwnerFaceTemplates), SRS EKYC-22, EKYC-26, EKYC-29, PRIV-03.
/// </summary>
/// <remarks>
/// - Chỉ lưu vector đặc trưng đã MÃ HÓA (envelope: DEK riêng bọc bằng KEK), không lưu ảnh. Vector là dữ liệu sinh trắc:
///   không trả cho FE, không log.
/// - Mẫu đầu tiên chỉ tạo từ selfie của phiên thử thách camera đã đạt trong lượt OWNER_ENROLLMENT (EKYC-22).
///   Mẫu đã có không bị thay bằng ảnh tải lên hay mật khẩu; MVP không có chức năng đổi mẫu (EKYC-23).
/// - Tách riêng khỏi danh tính đã gắn (EKYC-29) vì hạn giữ khác nhau.
/// - Mỗi người chỉ có một dòng đang dùng (dòng Version lớn nhất); Version tăng khi phải tạo lại sau khi mẫu bị xóa (EKYC-29).
/// </remarks>
public class OwnerFaceTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Tài khoản Owner sở hữu mẫu.</summary>
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Phiên bản mẫu của người này, bắt đầu từ 1 (EKYC-29). Unique theo (UserId, Version).</summary>
    public int Version { get; set; }

    /// <summary>Tên model trích đặc trưng (ví dụ "sface"). Vector của model khác nhau không so được với nhau (EKYC-15).</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Số chiều của vector, để biết cách đọc EmbeddingEncrypted mà không cần giải mã.</summary>
    public int Dimension { get; set; }

    /// <summary>Lượt eKYC OWNER_ENROLLMENT đạt đã sinh ra mẫu này. Không đặt khóa ngoại để dọn lượt eKYC cũ không làm mất mẫu.</summary>
    public Guid SourceVerificationId { get; set; }

    /// <summary>Id của KEK đã bọc WrappedDataKey (cho phép đổi KEK mà vẫn mở được mẫu cũ).</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK của mẫu, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>Đã mã hóa: các số thực 32 bit của vector đặc trưng (nonce 12 + dữ liệu + tag 16).</summary>
    public byte[] EmbeddingEncrypted { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; }
}
