namespace LegacyVault.Domain.Entities;

/// <summary>
/// Danh tính đã gắn của Owner (bảng OwnerIdentities), SRS EKYC-24, EKYC-27, EKYC-29, DATA-02.
/// </summary>
/// <remarks>
/// - Tạo một lần khi lượt OWNER_ENROLLMENT đầu tiên của tài khoản đạt; các lượt OWNER_ENROLLMENT sau phải ra đúng
///   số CCCD, họ tên, ngày sinh này (EKYC-24) nên dòng không bị ghi đè.
/// - CCCD đủ 12 số, họ tên, ngày sinh nằm trong IdentityEncrypted (JSON, mã hóa envelope). Chỉ 4 số cuối để dạng rõ.
/// - Hồ sơ chứng tử phải đối chiếu với danh tính này (DEATH-13).
/// - Tách riêng khỏi mẫu khuôn mặt (EKYC-29): danh tính được giữ cùng kho, không xóa.
/// - Mỗi tài khoản một dòng (unique theo UserId).
/// </remarks>
public class OwnerIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Tài khoản Owner sở hữu danh tính.</summary>
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>4 số cuối CCCD, dạng rõ (không đủ để định danh).</summary>
    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Lượt eKYC OWNER_ENROLLMENT đạt đã sinh ra danh tính này. Không đặt khóa ngoại để dọn lượt cũ không làm mất danh tính.</summary>
    public Guid SourceVerificationId { get; set; }

    /// <summary>Id của KEK đã bọc WrappedDataKey (cho phép đổi KEK mà vẫn mở được danh tính cũ).</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK của danh tính, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>Đã mã hóa: JSON {citizenId, fullName, dateOfBirth} (nonce 12 + dữ liệu + tag 16).</summary>
    public byte[] IdentityEncrypted { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Ảnh mặt trước CCCD của lượt eKYC đã gắn danh tính (BR-48 sửa 25/10/2026), để Verifier được giao hồ sơ chứng tử
    /// của kho đối chiếu. Nội dung nằm ở kho lưu trữ private, mã hóa envelope như tài sản; các cột DocumentImage* chỉ giữ
    /// tham chiếu và khóa đã bọc. Null DocumentImageStorageKey = chưa có ảnh (danh tính gắn trước khi có chức năng này).
    /// </summary>
    public string? DocumentImageStorageKey { get; set; }

    /// <summary>image/jpeg | image/png | image/webp, nhận theo nội dung thật của ảnh.</summary>
    public string? DocumentImageContentType { get; set; }

    public long? DocumentImageSizeBytes { get; set; }

    public string? DocumentImageEncryptionKeyId { get; set; }

    /// <summary>DEK của ảnh, đã bọc bằng KEK. Không trả cho FE, không log.</summary>
    public byte[]? DocumentImageWrappedDataKey { get; set; }

    /// <summary>Lúc chụp mặt trước (lúc tạo lượt eKYC).</summary>
    public DateTimeOffset? DocumentImageCapturedAt { get; set; }
}
