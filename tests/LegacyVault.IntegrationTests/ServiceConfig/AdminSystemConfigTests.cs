using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>
/// API Admin xem, lưu và khôi phục cấu hình dịch vụ ngoài: chỉ Admin đang hoạt động, khóa không lộ ở response hay nhật ký,
/// chống ghi đè đồng thời bằng version. Chạy API thật trên SQL Server database tạm.
/// </summary>
public sealed class AdminSystemConfigTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/v1/admin/system-config";
    private const string GeminiPrimary = BaseUrl + "/GEMINI/PRIMARY";
    private const string GeminiFallback = BaseUrl + "/GEMINI/FALLBACK";
    private const string Leak = "sk-test-LEAK-9999";

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

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program>? host = null)
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        WebApplicationFactory<Program> target = host ?? factory;
        HttpClient client = target.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, target.Services, admin);
        return client;
    }

    private WebApplicationFactory<Program> WithEnvKey(string key)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                Dictionary<string, string?> values = new Dictionary<string, string?>();
                values["Gemini:ApiKey"] = key;
                configuration.AddInMemoryCollection(values);
            });
        });
    }

    private static object Body(string model, string? apiKey, int version, string reason = "đổi cấu hình", bool enabled = true)
    {
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["apiKey"] = apiKey;
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["model"] = model;
        settings["timeoutSeconds"] = "20";
        return new { settings, secrets, enabled, reason, version };
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> ReadStoredSecretAsync(SystemConfigSlot slot)
    {
        string secret = string.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            SystemConfig row = await db.SystemConfigs.SingleAsync(c => c.Slot == slot);
            Dictionary<string, string> secrets =
                factory.Services.GetRequiredService<LegacyVault.Application.Security.ISystemConfigSecretProtector>().Unprotect(row);
            secret = secrets["apiKey"];
        });
        return secret;
    }

    [SqlFact]
    public async Task Get_ListsGeminiFromAppSettings_WithoutSecretValue()
    {
        using (WebApplicationFactory<Program> host = WithEnvKey("env-key-ABCDEFGH"))
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.GetAsync(BaseUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("env-key-ABCDEFGH", text);
            JsonElement gemini = JsonDocument.Parse(text).RootElement.GetProperty("services")[0];
            Assert.Equal("GEMINI", gemini.GetProperty("serviceKey").GetString());
            Assert.True(gemini.GetProperty("supportsFallback").GetBoolean());
            JsonElement primary = gemini.GetProperty("slots")[0];
            Assert.Equal("PRIMARY", primary.GetProperty("slot").GetString());
            Assert.Equal("APPSETTINGS", primary.GetProperty("source").GetString());
            Assert.Equal(0, primary.GetProperty("version").GetInt32());
            Assert.Equal("gemini-flash-lite-latest", primary.GetProperty("settings").GetProperty("model").GetString());
            JsonElement secret = primary.GetProperty("secrets")[0];
            Assert.Equal("apiKey", secret.GetProperty("name").GetString());
            Assert.True(secret.GetProperty("hasValue").GetBoolean());
            Assert.Equal(JsonValueKind.Null, secret.GetProperty("last4").ValueKind);
            JsonElement fallback = gemini.GetProperty("slots")[1];
            Assert.Equal("FALLBACK", fallback.GetProperty("slot").GetString());
            Assert.False(fallback.GetProperty("enabled").GetBoolean());
        }
    }

    [SqlFact]
    public async Task Put_CreatesRow_ReturnsVersion1_AndResolverSeesNewModel()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement slot = await ReadAsync(response);
        Assert.Equal(1, slot.GetProperty("version").GetInt32());
        Assert.Equal("DATABASE", slot.GetProperty("source").GetString());
        Assert.Equal("EFGH", slot.GetProperty("secrets")[0].GetProperty("last4").GetString());
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
            ResolvedConfig? config = await resolver.ResolveAsync("GEMINI", SystemConfigSlot.Primary, CancellationToken.None);
            Assert.Equal("gemini-2.5-flash", config!.Settings["model"]);
            Assert.Equal("DATABASE", config.Source);
        }
    }

    [SqlFact]
    public async Task Put_StaleVersion_Returns409EditConflict()
    {
        HttpClient client = await AdminClientAsync();
        await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));

        HttpResponseMessage stale = await client.PutAsJsonAsync(GeminiPrimary, Body("other-model", null, 0));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("EDIT_CONFLICT", (await ReadAsync(stale)).GetProperty("code").GetString());
    }

    [SqlFact]
    public async Task Put_BlankSecret_KeepsStoredSecret()
    {
        HttpClient client = await AdminClientAsync();
        await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));

        HttpResponseMessage second = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-pro", "", 1));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, (await ReadAsync(second)).GetProperty("version").GetInt32());
        Assert.Equal("key-ABCDEFGH", await ReadStoredSecretAsync(SystemConfigSlot.Primary));
    }

    [SqlFact]
    public async Task Put_FirstPrimarySaveWithBlankKey_CopiesAppSettingsKey()
    {
        using (WebApplicationFactory<Program> host = WithEnvKey("env-key-ABCDEFGH"))
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", null, 0));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("env-key-ABCDEFGH", await ReadStoredSecretAsync(SystemConfigSlot.Primary));
        }
    }

    [SqlFact]
    public async Task Put_FirstPrimarySaveWithoutAnyKey_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", null, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("secrets.apiKey", await response.Content.ReadAsStringAsync());
    }

    [SqlFact]
    public async Task Put_ModelWithPathInjection_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        foreach (string model in new[] { "a/../b", "m?key=1", "a b" })
        {
            HttpResponseMessage response = await client.PutAsJsonAsync(GeminiPrimary, Body(model, "key-ABCDEFGH", 0));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [SqlFact]
    public async Task Put_WithoutReason_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlFact]
    public async Task Put_FallbackEnabledWithoutKey_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(GeminiFallback, Body("gemini-2.5-flash", null, 0, enabled: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlFact]
    public async Task Put_FallbackDisabledWithoutKey_IsAccepted()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(GeminiFallback, Body("gemini-2.5-flash", null, 0, enabled: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SqlFact]
    public async Task Put_PrimaryDisabled_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0, enabled: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlFact]
    public async Task UnknownServiceOrSlot_Returns400()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage unknownService = await client.PutAsJsonAsync(BaseUrl + "/NOPE/PRIMARY", Body("m", "k", 0));
        HttpResponseMessage unknownSlot = await client.PutAsJsonAsync(BaseUrl + "/GEMINI/THIRD", Body("m", "k", 0));

        Assert.Equal(HttpStatusCode.BadRequest, unknownService.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownSlot.StatusCode);
    }

    [SqlFact]
    public async Task ResponsesAndAudit_NeverContainSecret()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage put = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", Leak, 0));
        HttpResponseMessage get = await client.GetAsync(BaseUrl);

        Assert.DoesNotContain(Leak, await put.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Leak, await get.Content.ReadAsStringAsync());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<string?> reasons = await db.AuditLogs.Select(a => a.Reason).ToListAsync();
            Assert.All(reasons, reason => Assert.DoesNotContain(Leak, reason ?? string.Empty));
            string settingsJson = await db.SystemConfigs.Select(c => c.SettingsJson).SingleAsync();
            Assert.DoesNotContain(Leak, settingsJson);
        });
    }

    [SqlFact]
    public async Task Put_WritesAuditWithFieldNamesOnly()
    {
        HttpClient client = await AdminClientAsync();

        await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0, "hết quota khóa cũ"));

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            AuditLog log = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.SystemConfigUpdated);
            Assert.Equal(AuditTargetType.Configuration, log.TargetType);
            Assert.Equal(AuditActorRole.Admin, log.ActorRole);
            Assert.Contains("GEMINI/PRIMARY", log.Reason);
            Assert.Contains("model", log.Reason);
            Assert.Contains("apiKey", log.Reason);
            Assert.Contains("hết quota khóa cũ", log.Reason);
            Assert.DoesNotContain("gemini-2.5-flash", log.Reason);
            Assert.DoesNotContain("key-ABCDEFGH", log.Reason);
        });
    }

    [SqlFact]
    public async Task Reset_RemovesRow_BackToAppSettings_AndWritesAudit()
    {
        HttpClient client = await AdminClientAsync();
        await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));

        HttpResponseMessage reset = await client.PostAsJsonAsync(GeminiPrimary + "/reset", new { reason = "quay về mặc định" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        JsonElement primary = JsonDocument.Parse(await (await client.GetAsync(BaseUrl)).Content.ReadAsStringAsync())
            .RootElement.GetProperty("services")[0].GetProperty("slots")[0];
        Assert.Equal("APPSETTINGS", primary.GetProperty("source").GetString());
        Assert.Equal(0, primary.GetProperty("version").GetInt32());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.SystemConfigs.CountAsync());
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.SystemConfigReset));
        });
    }

    [SqlFact]
    public async Task Reset_WithoutReason_Returns400_AndWhenNoRowIsNoOp()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage missingReason = await client.PostAsJsonAsync(GeminiPrimary + "/reset", new { reason = "" });
        HttpResponseMessage noRow = await client.PostAsJsonAsync(GeminiPrimary + "/reset", new { reason = "thử" });

        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, noRow.StatusCode);
    }

    /// <summary>Làm hỏng bản mã của khóa đã lưu, như khi khóa mã hóa KEK cũ đã bị bỏ khỏi cấu hình máy chủ.</summary>
    private async Task CorruptStoredSecretAsync()
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            SystemConfig row = await db.SystemConfigs.SingleAsync();
            byte[] broken = (byte[])row.SecretsEncrypted.Clone();
            broken[broken.Length - 1] ^= 0xFF;
            row.SecretsEncrypted = broken;
            await db.SaveChangesAsync();
        });
    }

    [SqlFact]
    public async Task Get_RowWithUnreadableSecret_StillListsAndReportsNoKey()
    {
        HttpClient client = await AdminClientAsync();
        await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));
        await CorruptStoredSecretAsync();

        HttpResponseMessage get = await client.GetAsync(BaseUrl);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        JsonElement primary = (await ReadAsync(get)).GetProperty("services")[0].GetProperty("slots")[0];
        Assert.Equal("DATABASE", primary.GetProperty("source").GetString());
        Assert.False(primary.GetProperty("secrets")[0].GetProperty("hasValue").GetBoolean());
    }

    [SqlFact]
    public async Task Put_RowWithUnreadableSecret_CanBeFixedByEnteringANewKey_ButNotByLeavingItBlank()
    {
        HttpClient client = await AdminClientAsync();
        await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));
        await CorruptStoredSecretAsync();

        HttpResponseMessage blank = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "", 1));
        HttpResponseMessage fixedKey = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "new-key-WXYZ1234", 1));

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fixedKey.StatusCode);
        Assert.Equal("new-key-WXYZ1234", await ReadStoredSecretAsync(SystemConfigSlot.Primary));
    }

    [SqlFact]
    public async Task NonAdminAndInactiveAdmin_Get404()
    {
        User verifier = await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
        User lockedAdmin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Locked);
        foreach (User user in new[] { verifier, lockedAdmin })
        {
            HttpClient client = factory.CreateClient();
            ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

            HttpResponseMessage get = await client.GetAsync(BaseUrl);
            HttpResponseMessage put = await client.PutAsJsonAsync(GeminiPrimary, Body("gemini-2.5-flash", "key-ABCDEFGH", 0));

            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        }
    }
}
