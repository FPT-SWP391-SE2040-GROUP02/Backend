namespace LegacyVault.Domain.Entities;

/// <summary>
/// Bộ đếm đăng nhập sai theo cặp email + dải IP (bảng LoginThrottles), SEC-10.
/// </summary>
/// <remarks>
/// SEC-10: 5 lần sai trong 15 phút thì cooldown 15 phút, theo nhiều tín hiệu (email + nguồn truy cập)
/// để người ngoài dò mật khẩu không khóa được tài khoản của chủ ở mọi nơi.
/// Email không tồn tại cũng được đếm như email thật (không lộ email nào đã đăng ký).
/// </remarks>
public class LoginThrottle
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Email đã chuẩn hóa; không cần là email đã đăng ký.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Dải IP của người gọi (ví dụ /24 với IPv4, /64 với IPv6).</summary>
    public string IpRange { get; set; } = string.Empty;

    public int FailedCount { get; set; }

    /// <summary>Mốc bắt đầu cửa sổ 15 phút đang đếm.</summary>
    public DateTimeOffset WindowStartedAt { get; set; }

    /// <summary>Đang khóa tới thời điểm này; null = không khóa.</summary>
    public DateTimeOffset? LockedUntil { get; set; }
}
