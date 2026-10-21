namespace LegacyVault.Domain.Entities;

/// <summary>
/// Khóa TOTP (RFC 6238) của một tài khoản Admin. Bí mật chỉ lưu dạng đã bọc bằng KEK, không bao giờ lưu rõ.
/// EnabledAt = null nghĩa là mới thiết lập, chưa xác nhận bằng mã đầu tiên nên chưa bắt buộc khi đăng nhập.
/// </summary>
public class UserTotp
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Mã khóa KEK đã bọc bí mật.</summary>
    public string KeyId { get; set; } = string.Empty;

    public byte[] WrappedSecret { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EnabledAt { get; set; }

    /// <summary>Time step của mã đã dùng gần nhất; mã có step nhỏ hơn hoặc bằng bị từ chối (chống dùng lại).</summary>
    public long LastUsedStep { get; set; }
}
