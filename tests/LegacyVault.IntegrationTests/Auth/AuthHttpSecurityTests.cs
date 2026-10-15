using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>HTTP checks chuyển từ test: proof thiếu/sai bị chặn trước service, CORS và rate limit thực.</summary>
public sealed class AuthHttpSecurityTests
{
    [Fact]
    public async Task JwtChallengeUsesProblemJsonEvenForUnsupportedAccept()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        var response = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SESSION_EXPIRED", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("logout")]
    [InlineData("google")]
    [InlineData("google/link-confirm")]
    public async Task MissingProofRejectsCookieMutation(string route)
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/" + route, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Auth.CallCount);
    }

    [Fact]
    public async Task ProofIsRawAndBodylessLogoutRequiresMatchingCookie()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var proof = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, proof.StatusCode);
        Assert.Contains("no-store", proof.Headers.CacheControl!.ToString());
        var body = await proof.Content.ReadFromJsonAsync<JsonElement>();
        string token = body.GetProperty("requestToken").GetString()!;
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.Contains(proof.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        client.DefaultRequestHeaders.Add(AuthCsrfContract.HeaderName, token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(1, factory.Auth.CallCount);

        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add(AuthCsrfContract.HeaderName, token);
        Assert.Equal(HttpStatusCode.BadRequest, (await otherClient.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(1, factory.Auth.CallCount);
    }

    [Fact]
    public async Task ValidProofPreservesBearerLoginAndRefreshCookie()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await AddProofAsync(client);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "user@example.test", password = "fixture" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fixture-access", body.GetProperty("accessToken").GetString());
        Assert.False(body.TryGetProperty("refreshToken", out _));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("lv_refresh="));
    }

    [Fact]
    public async Task LimiterBlocksBeforeServiceAndEmitsRetryAfter()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await AddProofAsync(client);
        for (int index = 0; index < AuthRateLimitPolicies.LoginPermitLimit; index++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "user@example.test", password = "fixture" })).StatusCode);
        }
        var blocked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "user@example.test", password = "fixture" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
        Assert.Equal(AuthRateLimitPolicies.LoginPermitLimit, factory.Auth.CallCount);
    }

    [Fact]
    public async Task CredentialsCorsOnlyAllowsConfiguredOrigin()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        var accepted = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal("true", accepted.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Equal("http://localhost:5173", accepted.Headers.GetValues("Access-Control-Allow-Origin").Single());
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var denied = await client.GetAsync("/api/v1/auth/csrf");
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static async Task AddProofAsync(HttpClient client)
    {
        var proof = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Add(AuthCsrfContract.HeaderName, proof.GetProperty("requestToken").GetString());
    }

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        public FakeAuthService Auth { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=127.0.0.1,1;Database=Unavailable;User Id=sa;Password=FixtureOnly;TrustServerCertificate=True;Connect Timeout=1",
                ["Email:Smtp:UserName"] = "", ["Email:Smtp:Password"] = "", ["CheckIn:Enabled"] = "false",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthService>();
                services.AddSingleton<IAuthService>(Auth);
            });
        }
    }

    private sealed class FakeAuthService : IAuthService
    {
        public int CallCount { get; private set; }
        public Task<AuthResult> LoginAsync(LoginRequest request, string ipRange, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new AuthResult { Session = new AuthSessionResponse { AccessToken = "fixture-access" }, RefreshToken = "fixture-refresh", RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(12) });
        }
        public Task<AuthResult> RefreshAsync(string? token, CancellationToken ct) => LoginAsync(new LoginRequest(), "fixture", ct);
        public Task LogoutAsync(string? token, bool all, CancellationToken ct) { CallCount++; return Task.CompletedTask; }
        public Task<UserResponse> GetCurrentUserAsync(CancellationToken ct) => Task.FromResult(new UserResponse());
    }
}
