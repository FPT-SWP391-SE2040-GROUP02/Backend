using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Tạo link trỏ về trang web (FE) để đặt vào email; địa chỉ web lấy từ App:ClientUrl.</summary>
public sealed class AppUrlProvider : IAppUrlProvider
{
    private readonly string _clientBaseUrl;

    public AppUrlProvider(IConfiguration configuration)
    {
        _clientBaseUrl = configuration["App:ClientUrl"]?.TrimEnd('/') ?? "http://localhost:5173";
    }

    // Token được mã hóa URL để ký tự đặc biệt không làm hỏng link.
    public string GetEmailVerificationUrl(string token)
    {
        return $"{_clientBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";
    }

    public string GetPasswordResetUrl(string token)
    {
        return $"{_clientBaseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
    }
}
