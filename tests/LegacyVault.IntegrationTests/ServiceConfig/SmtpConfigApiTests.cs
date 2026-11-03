using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.Email;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>
/// Email trên trang Admin: danh sách đọc đúng giá trị đang dùng (kể cả tên cấu hình cũ), lưu có luật kiểm tra, ô dự phòng chỉ cần
/// Webhook, nút thử kết nối không gửi thư và không lộ mật khẩu. Chạy API thật trên SQL Server tạm; đường SMTP dùng máy chủ giả.
/// </summary>
public sealed class SmtpConfigApiTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/v1/admin/system-config";
    private const string Primary = BaseUrl + "/SMTP/PRIMARY";
    private const string Fallback = BaseUrl + "/SMTP/FALLBACK";
    private const string Password = "smtp-pass-LEAK-8080";

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

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Responder { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program>? host = null)
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        WebApplicationFactory<Program> target = host ?? factory;
        HttpClient client = target.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, target.Services, admin);
        return client;
    }

    private static Dictionary<string, string?> Settings(
        string host = "smtp.example.test", string port = "587", string secureSocket = "StartTls",
        string userName = "", string webhookUrl = "")
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["host"] = host;
        settings["port"] = port;
        settings["secureSocket"] = secureSocket;
        settings["userName"] = userName;
        settings["fromAddress"] = "no-reply@example.test";
        settings["fromName"] = "LegacyVault";
        settings["timeoutSeconds"] = "20";
        settings["webhookUrl"] = webhookUrl;
        return settings;
    }

    private static object Body(Dictionary<string, string?> settings, string? password, int version, bool enabled = true)
    {
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["password"] = password;
        return new { settings, secrets, enabled, reason = "đổi cấu hình email", version };
    }

    [SqlFact]
    public async Task Get_ListsSmtpFromAppSettings_IncludingOldKeyNames_WithoutThePassword()
    {
        using (WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                Dictionary<string, string?> values = new Dictionary<string, string?>();
                values["SMTP__HOST"] = "alias.mail.test";
                values["Email:Smtp:Port"] = "465";
                values["Email:Smtp:UserName"] = "mailer@alias.mail.test";
                values["Email:Smtp:Password"] = Password;
                configuration.AddInMemoryCollection(values);
            });
        }))
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.GetAsync(BaseUrl);

            string text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Password, text);
            JsonElement smtp = JsonDocument.Parse(text).RootElement.GetProperty("services")
                .EnumerateArray().Single(s => s.GetProperty("serviceKey").GetString() == "SMTP");
            Assert.True(smtp.GetProperty("supportsFallback").GetBoolean());
            JsonElement primary = smtp.GetProperty("slots")[0];
            Assert.Equal("alias.mail.test", primary.GetProperty("settings").GetProperty("host").GetString());
            Assert.Equal("465", primary.GetProperty("settings").GetProperty("port").GetString());
            Assert.Equal("mailer@alias.mail.test", primary.GetProperty("settings").GetProperty("userName").GetString());
            Assert.True(primary.GetProperty("secrets")[0].GetProperty("hasValue").GetBoolean());
        }
    }

    [SqlFact]
    public async Task Put_SavesSettingsAndPassword_AndResolverSeesThem()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            Primary, Body(Settings(host: "smtp.sendgrid.test", port: "2525", userName: "apikey"), Password, 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Password, await response.Content.ReadAsStringAsync());
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
            ResolvedConfig? config = await resolver.ResolveAsync("SMTP", SystemConfigSlot.Primary, CancellationToken.None);
            Assert.Equal("smtp.sendgrid.test", config!.Settings["host"]);
            Assert.Equal("2525", config.Settings["port"]);
            Assert.Equal(Password, config.Secrets["password"]);
        }
    }

    [SqlFact]
    public async Task Put_RejectsBadPortSocketAndWebhook()
    {
        HttpClient client = await AdminClientAsync();
        List<Dictionary<string, string?>> bad = new List<Dictionary<string, string?>>
        {
            Settings(port: "0"),
            Settings(port: "70000"),
            Settings(port: "abc"),
            Settings(secureSocket: "ssl"),
            Settings(webhookUrl: "ftp://script.example.test"),
            Settings(webhookUrl: "https://user:pass@script.example.test"),
            Settings(host: ""),
        };

        foreach (Dictionary<string, string?> settings in bad)
        {
            HttpResponseMessage response = await client.PutAsJsonAsync(Primary, Body(settings, null, 0));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [SqlFact]
    public async Task Fallback_CanBeWebhookOnly_AndNeedsNoPassword()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            Fallback, Body(Settings(webhookUrl: "https://script.example.test/exec"), null, 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
            ResolvedConfig? config = await resolver.ResolveAsync("SMTP", SystemConfigSlot.Fallback, CancellationToken.None);
            Assert.Equal("https://script.example.test/exec", config!.Settings["webhookUrl"]);
        }
    }

    [SqlFact]
    public async Task TestEndpoint_Webhook_ReachableIsOk_AndServerErrorIsReported()
    {
        StubHandler handler = new StubHandler();
        using (WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StubFactory(handler));
        })))
        {
            HttpClient client = await AdminClientAsync(host);
            object draft = new
            {
                settings = Settings(webhookUrl: "https://script.example.test/exec"),
                secrets = new Dictionary<string, string?> { ["password"] = Password },
            };

            HttpResponseMessage ok = await client.PostAsJsonAsync(Primary + "/test", draft);
            handler.Responder = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            HttpResponseMessage down = await client.PostAsJsonAsync(Primary + "/test", draft);

            Assert.True((await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ok").GetBoolean());
            string text = await down.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Password, text);
            JsonElement body = JsonDocument.Parse(text).RootElement;
            Assert.False(body.GetProperty("ok").GetBoolean());
            Assert.Equal("HTTP_503", body.GetProperty("errorCode").GetString());
        }
    }

    [SqlFact]
    public async Task TestEndpoint_Smtp_ConnectsWithoutSendingAMail_AndReportsAnUnreachableServer()
    {
        using FakeSmtpServer server = new FakeSmtpServer();
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage reachable = await client.PostAsJsonAsync(Primary + "/test", new
        {
            settings = Settings(host: "127.0.0.1", port: server.Port.ToString(), secureSocket: "None"),
        });
        HttpResponseMessage unreachable = await client.PostAsJsonAsync(Primary + "/test", new
        {
            settings = Settings(host: "127.0.0.1", port: "1", secureSocket: "None"),
        });

        JsonElement ok = await reachable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Empty(server.ReceivedMessages);
        JsonElement failed = await unreachable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.NotNull(failed.GetProperty("errorCode").GetString());
    }
}
