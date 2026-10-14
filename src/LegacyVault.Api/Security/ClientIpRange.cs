using System.Net;
using System.Net.Sockets;

namespace LegacyVault.Api.Security;

/// <summary>
/// Tính dải IP của người gọi (IPv4 → /24, IPv6 → /64) để đếm đăng nhập sai theo SEC-10.
/// </summary>
/// <remarks>
/// Cách tính giống AuthController.GetClientIpRange để hai nơi đếm chung một bộ đếm.
/// Đặt ở file riêng để GoogleAuthController dùng mà không phải sửa AuthController.
/// </remarks>
public static class ClientIpRange
{
    public static string From(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;

        // Chạy sau reverse proxy thì IP thật nằm ở phần tử đầu của X-Forwarded-For.
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            if (IPAddress.TryParse(firstIp, out var parsedIp))
            {
                ip = parsedIp;
            }
        }

        if (ip == null)
        {
            return "unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Giữ 8 byte đầu (64 bit), phần còn lại để 0.
            var prefixBytes = new byte[16];
            Array.Copy(ip.GetAddressBytes(), prefixBytes, 8);
            return $"{new IPAddress(prefixBytes)}/64";
        }

        return "unknown";
    }
}
