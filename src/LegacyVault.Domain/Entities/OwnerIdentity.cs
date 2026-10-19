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
}
