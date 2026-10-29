using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Tài khoản người dùng (bảng Users), SRS §11.6 "Tài khoản".
/// </summary>
/// <remarks>
/// - Id là định danh ổn định của tài khoản; KHÔNG lấy từ số căn cước.
/// - Email luôn lưu dạng đã chuẩn hóa (trim, chữ thường), duy nhất.
/// - Tài khoản chỉ đăng nhập Google thì PasswordHash = null (AUTH-03); liên kết Google nằm ở ExternalLogin.
/// </remarks>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Email { get; set; } = string.Empty;

    /// <summary>Thời điểm xác minh email (AUTH-04); null = chưa xác minh → chưa tạo được kho (AC-01).</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>Mật khẩu đã băm SHA-256 kèm salt (SEC-01). null = tài khoản không có mật khẩu app.</summary>
    public string? PasswordHash { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public UserRole Role { get; set; }

    public UserStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Ảnh đại diện (tùy chọn). Nội dung nằm ở kho lưu trữ private, mã hóa envelope như tài sản; các cột Avatar* chỉ giữ
    /// tham chiếu và khóa đã bọc. Null AvatarStorageKey = chưa có ảnh.
    /// </summary>
    public string? AvatarStorageKey { get; set; }

    /// <summary>"image/jpeg", "image/png" hoặc "image/webp", nhận theo nội dung thật của tệp.</summary>
    public string? AvatarContentType { get; set; }

    public long? AvatarSizeBytes { get; set; }

    public string? AvatarEncryptionKeyId { get; set; }

    /// <summary>DEK của ảnh, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[]? AvatarWrappedDataKey { get; set; }

    /// <summary>Lần đổi ảnh gần nhất; FE dùng làm khóa làm mới ảnh trong bộ nhớ.</summary>
    public DateTimeOffset? AvatarUpdatedAt { get; set; }
}
