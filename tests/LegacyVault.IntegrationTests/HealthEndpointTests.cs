using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LegacyVault.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task LiveStaysHealthyButReadyFailsWhenDatabaseIsUnavailable()
    {
        // The test owns no SQL resources. TCP port 1 deliberately has no SQL Server.
        await using var factory = new ApiFactory(
            "Server=127.0.0.1,1;Database=Unavailable;User Id=sa;Password=TestOnly123!;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1");
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());

        var openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        var missing = await client.GetAsync("/api/v1/not-implemented");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [SqlFact]
    public async Task ReadySucceedsAgainstConfiguredSqlServer()
    {
        var connectionString = Environment.GetEnvironmentVariable("LEGACYVAULT_TEST_CONNECTION")!;
        await using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = connectionString,
                    // Health test có thể kết nối DB development: tuyệt đối không chạy job hoặc SMTP thật.
                    ["CheckIn:Enabled"] = "false",
                    ["Email:Smtp:UserName"] = "",
                    ["Email:Smtp:Password"] = "",
                }));
        }
    }
}

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LEGACYVAULT_TEST_CONNECTION")))
        {
            Skip = "Set LEGACYVAULT_TEST_CONNECTION to run the real SQL Server readiness test.";
        }
    }
}
