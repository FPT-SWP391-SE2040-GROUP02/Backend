namespace LegacyVault.Application.Services;

/// <summary>
/// Kiểm tra ReturnUrl sau đăng nhập chỉ là đường dẫn NỘI BỘ (AUTH-06).
/// </summary>
/// <remarks>
/// Nếu nhận cả URL ngoài, kẻ xấu có thể gửi link đăng nhập thật nhưng sau đó chuyển người dùng sang trang giả
/// (open redirect). Vì vậy chỉ giữ chuỗi bắt đầu bằng một dấu "/".
/// </remarks>
public static class ReturnUrlValidator
{
    /// <summary>Trả lại url nếu là đường dẫn nội bộ, ngược lại trả null.</summary>
    public static string? KeepIfLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        // Phải bắt đầu bằng "/" (ví dụ "/invitations/abc").
        if (!url.StartsWith('/'))
        {
            return null;
        }

        // "//evil.com" và "/\evil.com" bị trình duyệt hiểu là sang tên miền khác.
        if (url.StartsWith("//") || url.StartsWith("/\\"))
        {
            return null;
        }

        // Ký tự điều khiển (xuống dòng, tab...) có thể bị dùng để lách kiểm tra phía trên.
        if (url.Any(char.IsControl))
        {
            return null;
        }

        return url;
    }
}
