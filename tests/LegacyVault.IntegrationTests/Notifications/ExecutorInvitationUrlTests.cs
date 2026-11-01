using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LegacyVault.IntegrationTests.Notifications;

public sealed class ExecutorInvitationUrlTests
{
    [Fact]
    public void GetExecutorInvitationUrl_WhenConfigEmpty_FallsBackToDefaultClientUrlWithQueryParam()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:ClientUrl"] = ""
            })
            .Build();

        var provider = new AppUrlProvider(config);
        var url = provider.GetExecutorInvitationUrl("test-token-123");

        Assert.Equal("http://localhost:5173/executor-invitation?token=test-token-123", url);
    }

    [Fact]
    public void GetExecutorInvitationUrl_WhenConfigProvided_UsesConfiguredBaseUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:ClientUrl"] = "https://vault.example.com/"
            })
            .Build();

        var provider = new AppUrlProvider(config);
        var url = provider.GetExecutorInvitationUrl("abc/xyz+123==");

        Assert.StartsWith("https://vault.example.com/executor-invitation?token=", url);
        Assert.Contains(Uri.EscapeDataString("abc/xyz+123=="), url);
    }
}
