using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

public sealed class AppUrlProvider : IAppUrlProvider
{
    private readonly string _clientBaseUrl;

    public AppUrlProvider(IConfiguration configuration)
    {
        _clientBaseUrl = configuration["App:ClientUrl"]?.TrimEnd('/') ?? "http://localhost:5173";
    }

    public string GetEmailVerificationUrl(string token)
    {
        return $"{_clientBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";
    }
}
