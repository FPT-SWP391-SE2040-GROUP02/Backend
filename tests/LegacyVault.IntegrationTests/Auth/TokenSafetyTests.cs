using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

public sealed class TokenSafetyTests
{
    private static JwtTokenService Service() => new(Options.Create(new JwtSettings { Issuer = "fixture", Audience = "fixture", SigningKey = new string('k', 32) }), TimeProvider.System);

    [Fact]
    public void InvalidIdentityCannotReceiveJwt()
    {
        Assert.Throws<ArgumentException>(() => Service().CreateAccessToken(new User { Id = Guid.Empty, Role = UserRole.User }));
        Assert.Throws<ArgumentException>(() => Service().CreateAccessToken(new User { Role = (UserRole)99 }));
    }

    [Fact]
    public void OpaqueTokensKeepBackendFormatAndAreHashedWithoutTrimming()
    {
        var service = Service();
        var token = service.CreateOpaqueToken();
        Assert.Equal(64, token.Value.Length);
        Assert.Equal(token.Hash, service.HashOpaqueToken(token.Value));
        Assert.NotEqual(service.HashOpaqueToken(token.Value), service.HashOpaqueToken(" " + token.Value));
    }
}
