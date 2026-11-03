using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// SePay (tài khoản nhận tiền, môi trường Test/Live, khóa webhook) sửa được trên trang Admin và có hiệu lực ngay.
/// Chuyển sang Live phải gõ lại tên dịch vụ. SePay không có ô dự phòng và không có nút thử kết nối. Chạy API thật trên SQL Server tạm.
/// </summary>
public sealed class SePayConfigFromWebTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/v1/admin/system-config";
    private const string SePayUrl = BaseUrl + "/SEPAY/PRIMARY";
    private const string WebhookUrl = "/api/v1/billing/sepay/webhook";
    private const string NewKey = "new-sepay-key-LEAK-0001";

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

    private async Task<HttpClient> AdminClientAsync()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return client;
    }

    private static object Body(
        string environment, string? webhookKey, int version, string? confirmText = null,
        string account = "1234567890")
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["environment"] = environment;
        settings["bankCode"] = "MBBank";
        settings["accountNumber"] = account;
        settings["accountName"] = "CONG TY A";
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["webhookApiKey"] = webhookKey;
        return new { settings, secrets, enabled = true, reason = "đổi cấu hình SePay", version, confirmText };
    }

    private async Task<HttpStatusCode> PostWebhookAsync(string apiKey)
    {
        HttpClient client = factory.CreateClient();
        Dictionary<string, object?> payload = new Dictionary<string, object?>();
        payload["id"] = 987001;
        payload["accountNumber"] = "0000000000";
        payload["transferType"] = "in";
        payload["transferAmount"] = 199000;
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl);
        request.Content = JsonContent.Create(payload);
        request.Headers.TryAddWithoutValidation("Authorization", "Apikey " + apiKey);
        HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [SqlFact]
    public async Task NoRow_BankAndKeyComeFromAppSettings()
    {
        using (WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                Dictionary<string, string?> values = new Dictionary<string, string?>();
                values["Billing:Bank:BankCode"] = "MBBank";
                values["Billing:Bank:AccountNumber"] = "5550001111";
                values["Billing:Bank:AccountName"] = "CONG TY MAC DINH";
                configuration.AddInMemoryCollection(values);
            });
        }))
        {
            using (IServiceScope scope = host.Services.CreateScope())
            {
                IBillingBankSettings bank = scope.ServiceProvider.GetRequiredService<IBillingBankSettings>();
                IBillingSePaySettings sePay = scope.ServiceProvider.GetRequiredService<IBillingSePaySettings>();
                Assert.Equal("5550001111", bank.AccountNumber);
                Assert.Equal("MBBank", bank.BankCode);
                Assert.Equal("CONG TY MAC DINH", bank.AccountName);
                Assert.Equal(ApiFactory.SePayTestApiKey, sePay.WebhookApiKey);
                Assert.Equal("Test", sePay.Environment);
            }
        }
    }

    [SqlFact]
    public async Task Get_ListsSePayWithoutFallbackOrKeyValue()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.GetAsync(BaseUrl);

        string text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiFactory.SePayTestApiKey, text);
        JsonElement sePay = JsonDocument.Parse(text).RootElement.GetProperty("services")
            .EnumerateArray().Single(s => s.GetProperty("serviceKey").GetString() == "SEPAY");
        Assert.False(sePay.GetProperty("supportsFallback").GetBoolean());
        Assert.Single(sePay.GetProperty("slots").EnumerateArray());
        JsonElement primary = sePay.GetProperty("slots")[0];
        Assert.Equal("Test", primary.GetProperty("settings").GetProperty("environment").GetString());
        Assert.True(primary.GetProperty("secrets")[0].GetProperty("hasValue").GetBoolean());
    }

    [SqlFact]
    public async Task Put_ChangesAccountAndWebhookKey_TakeEffectImmediately()
    {
        HttpClient client = await AdminClientAsync();
        Assert.NotEqual(HttpStatusCode.Unauthorized, await PostWebhookAsync(ApiFactory.SePayTestApiKey));

        HttpResponseMessage response = await client.PutAsJsonAsync(SePayUrl, Body("Test", NewKey, 0, account: "9998887776"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(NewKey, await response.Content.ReadAsStringAsync());
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IBillingBankSettings bank = scope.ServiceProvider.GetRequiredService<IBillingBankSettings>();
            IBillingSePaySettings sePay = scope.ServiceProvider.GetRequiredService<IBillingSePaySettings>();
            Assert.Equal("9998887776", bank.AccountNumber);
            Assert.Equal(NewKey, sePay.WebhookApiKey);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await PostWebhookAsync(ApiFactory.SePayTestApiKey));
        Assert.NotEqual(HttpStatusCode.Unauthorized, await PostWebhookAsync(NewKey));
    }

    [SqlFact]
    public async Task Put_SwitchToLive_NeedsConfirmText_ThenLiveEditsDoNot()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage missing = await client.PutAsJsonAsync(SePayUrl, Body("Live", null, 0));
        HttpResponseMessage wrong = await client.PutAsJsonAsync(SePayUrl, Body("Live", null, 0, "R2"));
        HttpResponseMessage confirmed = await client.PutAsJsonAsync(SePayUrl, Body("Live", null, 0, " sepay "));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("confirmText", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            Assert.Equal("Live", scope.ServiceProvider.GetRequiredService<IBillingSePaySettings>().Environment);
        }
        HttpResponseMessage laterEdit = await client.PutAsJsonAsync(SePayUrl, Body("Live", null, 1, account: "1112223334"));
        Assert.Equal(HttpStatusCode.OK, laterEdit.StatusCode);
    }

    [SqlFact]
    public async Task Put_InvalidEnvironment_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        foreach (string environment in new[] { "Prod", "test", "" })
        {
            HttpResponseMessage response = await client.PutAsJsonAsync(SePayUrl, Body(environment, null, 0, "SEPAY"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [SqlFact]
    public async Task Fallback_AndTestConnection_AreNotSupported()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage fallback = await client.PutAsJsonAsync(BaseUrl + "/SEPAY/FALLBACK", Body("Test", null, 0));
        HttpResponseMessage test = await client.PostAsJsonAsync(SePayUrl + "/test", new
        {
            settings = new Dictionary<string, string?>
            {
                ["environment"] = "Test", ["bankCode"] = "MBBank", ["accountNumber"] = "1", ["accountName"] = "A",
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, fallback.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, test.StatusCode);
    }

    [SqlFact]
    public async Task Reset_GoesBackToAppSettings()
    {
        HttpClient client = await AdminClientAsync();
        await client.PutAsJsonAsync(SePayUrl, Body("Test", NewKey, 0, account: "9998887776"));

        HttpResponseMessage reset = await client.PostAsJsonAsync(SePayUrl + "/reset", new { reason = "về mặc định" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            Assert.Equal(ApiFactory.SePayTestApiKey, scope.ServiceProvider.GetRequiredService<IBillingSePaySettings>().WebhookApiKey);
        }
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.SystemConfigs.CountAsync());
        });
    }
}
