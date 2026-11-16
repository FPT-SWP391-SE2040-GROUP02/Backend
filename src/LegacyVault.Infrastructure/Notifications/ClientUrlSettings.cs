using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Đọc địa chỉ giao diện web (App:ClientUrl) để ghép link trong email.
/// Chưa cấu hình thì dùng địa chỉ chạy local.
/// </summary>
public static class ClientUrlSettings
{
    private const string DefaultClientUrl = "http://localhost:5173";

    public static string Read(IConfiguration configuration)
    {
        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            return DefaultClientUrl;
        }
        return clientUrl;
    }
}
