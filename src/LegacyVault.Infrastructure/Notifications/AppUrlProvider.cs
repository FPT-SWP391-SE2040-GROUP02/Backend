using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

public sealed class AppUrlProvider : IAppUrlProvider
{
    private readonly string _clientBaseUrl;

    public AppUrlProvider(IConfiguration configuration)
    {
        string? url = configuration["App:ClientUrl"]
            ?? configuration["App__ClientUrl"]
            ?? configuration["APP_CLIENTURL"]
            ?? configuration["APP__CLIENTURL"];

        if (string.IsNullOrWhiteSpace(url))
        {
            string? env = configuration["ASPNETCORE_ENVIRONMENT"];
            url = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase)
                ? "https://legacyvault-swart.vercel.app"
                : "http://localhost:5173";
        }

        _clientBaseUrl = url.TrimEnd('/');
    }

    public string GetEmailVerificationUrl(string token)
    {
        return $"{_clientBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";
    }

    public string GetPasswordResetUrl(string token)
    {
        return $"{_clientBaseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
    }
}
