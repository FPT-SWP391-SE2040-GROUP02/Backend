using LegacyVault.Api.Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>Cấu hình tin proxy: mặc định chỉ loopback, chỉ bật tin mọi proxy khi cấu hình cho phép (Render).</summary>
public sealed class ForwardedHeadersConfigTests
{
    private static IConfiguration Config(string? trustAnyProxy)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>();
        if (trustAnyProxy != null)
        {
            values[ForwardedHeadersConfig.TrustAnyProxyKey] = trustAnyProxy;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Default_KeepsLoopbackOnlyTrust()
    {
        var options = ForwardedHeadersConfig.Create(Config(null));

        Assert.NotEmpty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void TrustAnyProxy_ClearsKnownProxiesAndNetworksButKeepsForwardLimit()
    {
        var options = ForwardedHeadersConfig.Create(Config("true"));

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.Equal(1, options.ForwardLimit);
    }
}
