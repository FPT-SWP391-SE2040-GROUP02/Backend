using System.Net;
using System.Net.Sockets;

namespace LegacyVault.Application.Security;

/// <summary>
/// Tính dải IP của người gọi (IPv4 → /24, IPv6 → /64) để đếm đăng nhập sai theo SEC-10.
/// Một nơi duy nhất để AuthController, GoogleAuthController và CurrentUser đếm chung một bộ đếm.
/// </summary>
public static class IpRange
{
    public static string From(IPAddress? ip)
    {
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
            byte[] bytes = ip.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Giữ 8 byte đầu (64 bit), phần còn lại để 0.
            byte[] prefixBytes = new byte[16];
            Array.Copy(ip.GetAddressBytes(), prefixBytes, 8);
            return $"{new IPAddress(prefixBytes)}/64";
        }

        return "unknown";
    }
}
