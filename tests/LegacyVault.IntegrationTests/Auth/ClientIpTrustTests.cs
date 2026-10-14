using System.Net;
using LegacyVault.Api.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

public sealed class ClientIpTrustTests
{
    [Fact]
    public void SpoofedForwardedHeaderDoesNotBypassThrottlePartition()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.5";
        Assert.Equal("192.0.2.0/24", ClientIpRange.From(context));
    }
}
