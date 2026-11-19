using LegacyVault.Application.Security;

namespace LegacyVault.Api.Security;

/// <summary>
/// Dải IP của người gọi để đếm đăng nhập sai theo SEC-10 (tính ở <see cref="IpRange"/>).
/// </summary>
/// <remarks>
/// Chỉ dùng IP do server xác định. Forwarded headers chỉ được áp dụng bởi middleware
/// khi có trusted proxy đã cấu hình; không tin header do caller tự gửi.
/// </remarks>
public static class ClientIpRange
{
    public static string From(HttpContext context)
    {
        return IpRange.From(context.Connection.RemoteIpAddress);
    }
}
