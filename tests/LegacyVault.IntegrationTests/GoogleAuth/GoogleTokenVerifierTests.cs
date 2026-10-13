using LegacyVault.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.GoogleAuth;

/// <summary>Test các trường hợp không cần gọi mạng của GoogleTokenVerifier thật.</summary>
public sealed class GoogleTokenVerifierTests
{
    [Fact]
    public async Task MalformedToken_ReturnsNull()
    {
        var verifier = CreateVerifier("test-client-id.apps.googleusercontent.com");

        var identity = await verifier.VerifyAsync("khong-phai-jwt", null, default);

        Assert.Null(identity);
    }

    [Fact]
    public async Task MissingClientId_FailsLoudly()
    {
        var verifier = CreateVerifier("");

        await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync("bat-ky", null, default));
    }

    private static GoogleTokenVerifier CreateVerifier(string clientId)
    {
        return new GoogleTokenVerifier(Options.Create(new GoogleAuthSettings { ClientId = clientId }));
    }
}
