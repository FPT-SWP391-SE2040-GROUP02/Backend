using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>
/// Nút "Thử kết nối bằng giá trị đang nhập": thử giá trị nháp mà không lưu, khóa để trống thì dùng khóa đang lưu,
/// không lộ khóa, giới hạn 10 lần/phút. Chạy API thật trên SQL Server database tạm.
/// </summary>
public sealed class AdminSystemConfigTestEndpointTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/v1/admin/system-config";
    private const string GeminiPrimary = BaseUrl + "/EKYC/PRIMARY";
    private const string TestUrl = GeminiPrimary + "/test";
    private const string DraftKey = "draft-key-LEAK-7777";

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private sealed class FakeTester : IServiceConnectionTester
    {
        public string ServiceKey
        {
            get { return SystemConfigServices.Ekyc; }
        }

        public ResolvedConfig? Draft { get; private set; }

        public Task<TestSystemConfigResponse> TestAsync(ResolvedConfig draft, CancellationToken ct)
        {
            Draft = draft;
            TestSystemConfigResponse response = new TestSystemConfigResponse();
            response.Ok = true;
            response.LatencyMs = 12;
            return Task.FromResult(response);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Responder { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Responder());
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public StubFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    private WebApplicationFactory<Program> WithFakeTester(FakeTester tester)
    {
        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceConnectionTester>();
            services.AddSingleton<IServiceConnectionTester>(tester);
        }));
    }

    private WebApplicationFactory<Program> WithHttp(StubHandler handler)
    {
        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StubFactory(handler));
        }));
    }

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> host)
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = host.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, host.Services, admin);
        return client;
    }

    private static object Draft(string model, string? apiKey, string timeout = "20")
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["baseUrl"] = "http://" + model + ".test";
        settings["timeoutSeconds"] = timeout;
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["apiKey"] = apiKey;
        return new { settings, secrets };
    }

    private static object EkycDraft(string baseUrl, string? apiKey)
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["baseUrl"] = baseUrl;
        settings["timeoutSeconds"] = "20";
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["apiKey"] = apiKey;
        return new { settings, secrets };
    }

    private async Task SaveKeyAsync(HttpClient client, string apiKey)
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["baseUrl"] = "http://gemini-flash-lite-latest.test";
        settings["timeoutSeconds"] = "20";
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["apiKey"] = apiKey;
        HttpResponseMessage saved = await client.PutAsJsonAsync(
            GeminiPrimary, new { settings, secrets, enabled = true, reason = "lưu khóa", version = 0 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [SqlFact]
    public async Task Test_UsesDraftValues_AndDoesNotWriteRowOrAudit()
    {
        FakeTester tester = new FakeTester();
        using (WebApplicationFactory<Program> host = WithFakeTester(tester))
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.PostAsJsonAsync(TestUrl, Draft("draft-model", DraftKey, "30"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("ok").GetBoolean());
            Assert.Equal(12, body.GetProperty("latencyMs").GetInt32());
            Assert.Equal("http://draft-model.test", tester.Draft!.Settings["baseUrl"]);
            Assert.Equal("30", tester.Draft.Settings["timeoutSeconds"]);
            Assert.Equal(DraftKey, tester.Draft.Secrets["apiKey"]);
            await ApiTestHelpers.WithDbAsync(factory, async db =>
            {
                Assert.Equal(0, await db.SystemConfigs.CountAsync());
                Assert.Equal(0, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.SystemConfigUpdated));
            });
        }
    }

    [SqlFact]
    public async Task Test_BlankSecret_UsesStoredKey()
    {
        FakeTester tester = new FakeTester();
        using (WebApplicationFactory<Program> host = WithFakeTester(tester))
        {
            HttpClient client = await AdminClientAsync(host);
            await SaveKeyAsync(client, "stored-key-ABCDEFGH");

            HttpResponseMessage response = await client.PostAsJsonAsync(TestUrl, Draft("gemini-2.5-flash", ""));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("stored-key-ABCDEFGH", tester.Draft!.Secrets["apiKey"]);
            Assert.Equal("http://gemini-2.5-flash.test", tester.Draft.Settings["baseUrl"]);
        }
    }

    [SqlFact]
    public async Task RealEkycTester_ReachableService_IsOk_EvenWhenRootIs404()
    {
        StubHandler handler = new StubHandler();
        handler.Responder = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        using (WebApplicationFactory<Program> host = WithHttp(handler))
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.PostAsJsonAsync(
                BaseUrl + "/EKYC/PRIMARY/test", EkycDraft("http://ekyc.example.test:8879", null));

            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("ok").GetBoolean());
            Assert.Equal(1, handler.Calls);
        }
    }

    [SqlFact]
    public async Task RealEkycTester_Rejected401_ReportsErrorCodeWithoutKey()
    {
        StubHandler handler = new StubHandler();
        handler.Responder = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using (WebApplicationFactory<Program> host = WithHttp(handler))
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.PostAsJsonAsync(
                BaseUrl + "/EKYC/PRIMARY/test", EkycDraft("http://ekyc.example.test:8879", DraftKey));

            string text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(DraftKey, text);
            JsonElement body = JsonDocument.Parse(text).RootElement;
            Assert.False(body.GetProperty("ok").GetBoolean());
            Assert.Equal("HTTP_401", body.GetProperty("errorCode").GetString());
        }
    }

    [SqlFact]
    public async Task RealEkycTester_DownOrTimeout_ReportsNetworkOrTimeout()
    {
        StubHandler network = new StubHandler();
        network.Responder = () => throw new HttpRequestException("down");
        StubHandler slow = new StubHandler();
        slow.Responder = () => throw new TaskCanceledException("timeout");

        foreach ((StubHandler handler, string expected) in new[] { (network, "NETWORK"), (slow, "TIMEOUT") })
        {
            using (WebApplicationFactory<Program> host = WithHttp(handler))
            {
                HttpClient client = await AdminClientAsync(host);

                HttpResponseMessage response = await client.PostAsJsonAsync(
                    BaseUrl + "/EKYC/PRIMARY/test", EkycDraft("http://ekyc.example.test:8879", null));

                JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(body.GetProperty("ok").GetBoolean());
                Assert.Equal(expected, body.GetProperty("errorCode").GetString());
            }
        }
    }

    [SqlFact]
    public async Task Test_NonAdmin_Returns404()
    {
        FakeTester tester = new FakeTester();
        using (WebApplicationFactory<Program> host = WithFakeTester(tester))
        {
            User verifier = await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
            HttpClient client = host.CreateClient();
            ApiTestHelpers.AuthorizeAs(client, host.Services, verifier);

            HttpResponseMessage response = await client.PostAsJsonAsync(TestUrl, Draft("gemini-2.5-flash", DraftKey));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(tester.Draft);
        }
    }

    [SqlFact]
    public async Task Test_RateLimited_After10Calls()
    {
        FakeTester tester = new FakeTester();
        using (WebApplicationFactory<Program> host = WithFakeTester(tester))
        {
            HttpClient client = await AdminClientAsync(host);

            for (int i = 0; i < 10; i++)
            {
                HttpResponseMessage ok = await client.PostAsJsonAsync(TestUrl, Draft("gemini-2.5-flash", DraftKey));
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }
            HttpResponseMessage limited = await client.PostAsJsonAsync(TestUrl, Draft("gemini-2.5-flash", DraftKey));

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }
    }
}
