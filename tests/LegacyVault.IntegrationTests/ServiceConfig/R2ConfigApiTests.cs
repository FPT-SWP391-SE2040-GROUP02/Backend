using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
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
/// R2 trên trang Admin: danh sách đọc đúng giá trị đang dùng mà không lộ khóa, đổi tài khoản hoặc bucket phải gõ lại tên dịch vụ
/// (Backend kiểm), tên tài khoản và bucket chặt chẽ vì tài khoản nằm trong tên máy của địa chỉ gọi, lần lưu đầu giữ khóa từ
/// appsettings. Chạy API thật trên SQL Server tạm.
/// </summary>
public sealed class R2ConfigApiTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/v1/admin/system-config";
    private const string R2Url = BaseUrl + "/R2/PRIMARY";
    private const string EnvAccessKey = "env-access-LEAK-1111";
    private const string EnvSecretKey = "env-secret-LEAK-2222";

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

    private WebApplicationFactory<Program> WithR2AppSettings()
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                Dictionary<string, string?> values = new Dictionary<string, string?>();
                values["AssetStorage:R2:AccountId"] = "acc1";
                values["AssetStorage:R2:BucketName"] = "bucket-one";
                values["AssetStorage:R2:AccessKeyId"] = EnvAccessKey;
                values["AssetStorage:R2:SecretAccessKey"] = EnvSecretKey;
                configuration.AddInMemoryCollection(values);
            });
        });
    }

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program>? host = null)
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        WebApplicationFactory<Program> target = host ?? factory;
        HttpClient client = target.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, target.Services, admin);
        return client;
    }

    private static object Body(
        string accountId, string bucket, int version, string? accessKeyId = null, string? secretKey = null,
        string? confirmText = null)
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["accountId"] = accountId;
        settings["bucketName"] = bucket;
        Dictionary<string, string?> secrets = new Dictionary<string, string?>();
        secrets["accessKeyId"] = accessKeyId;
        secrets["secretAccessKey"] = secretKey;
        return new { settings, secrets, enabled = true, reason = "đổi cấu hình R2", version, confirmText };
    }

    private async Task<Dictionary<string, string>> ReadStoredSecretsAsync(WebApplicationFactory<Program> host)
    {
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            SystemConfig row = await db.SystemConfigs.SingleAsync(c => c.ServiceKey == "R2");
            secrets = host.Services.GetRequiredService<ISystemConfigSecretProtector>().Unprotect(row);
        });
        return secrets;
    }

    [SqlFact]
    public async Task Get_ListsR2FromAppSettings_WithoutTheKeys_AndWithoutFallback()
    {
        using (WebApplicationFactory<Program> host = WithR2AppSettings())
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.GetAsync(BaseUrl);

            string text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(EnvAccessKey, text);
            Assert.DoesNotContain(EnvSecretKey, text);
            JsonElement r2 = JsonDocument.Parse(text).RootElement.GetProperty("services")
                .EnumerateArray().Single(s => s.GetProperty("serviceKey").GetString() == "R2");
            Assert.False(r2.GetProperty("supportsFallback").GetBoolean());
            Assert.Single(r2.GetProperty("slots").EnumerateArray());
            JsonElement primary = r2.GetProperty("slots")[0];
            Assert.Equal("acc1", primary.GetProperty("settings").GetProperty("accountId").GetString());
            Assert.Equal("bucket-one", primary.GetProperty("settings").GetProperty("bucketName").GetString());
            Assert.All(primary.GetProperty("secrets").EnumerateArray(), secret => Assert.True(secret.GetProperty("hasValue").GetBoolean()));
        }
    }

    [SqlFact]
    public async Task Put_ChangingTheBucket_NeedsConfirmText_AndTheFirstSaveKeepsTheKeysFromAppSettings()
    {
        using (WebApplicationFactory<Program> host = WithR2AppSettings())
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage missing = await client.PutAsJsonAsync(R2Url, Body("acc1", "bucket-two", 0));
            HttpResponseMessage wrong = await client.PutAsJsonAsync(R2Url, Body("acc1", "bucket-two", 0, confirmText: "SEPAY"));
            HttpResponseMessage confirmed = await client.PutAsJsonAsync(R2Url, Body("acc1", "bucket-two", 0, confirmText: " r2 "));

            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Contains("confirmText", await missing.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            string text = await confirmed.Content.ReadAsStringAsync();
            Assert.DoesNotContain(EnvAccessKey, text);
            Dictionary<string, string> stored = await ReadStoredSecretsAsync(host);
            Assert.Equal(EnvAccessKey, stored["accessKeyId"]);
            Assert.Equal(EnvSecretKey, stored["secretAccessKey"]);
            using (IServiceScope scope = host.Services.CreateScope())
            {
                ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
                ResolvedConfig? config = await resolver.ResolveAsync("R2", SystemConfigSlot.Primary, CancellationToken.None);
                Assert.Equal("bucket-two", config!.Settings["bucketName"]);
            }
        }
    }

    [SqlFact]
    public async Task Put_ChangingOnlyTheKeys_NeedsNoConfirmText()
    {
        using (WebApplicationFactory<Program> host = WithR2AppSettings())
        {
            HttpClient client = await AdminClientAsync(host);

            HttpResponseMessage response = await client.PutAsJsonAsync(
                R2Url, Body("acc1", "bucket-one", 0, "rotated-access", "rotated-secret"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Dictionary<string, string> stored = await ReadStoredSecretsAsync(host);
            Assert.Equal("rotated-access", stored["accessKeyId"]);
            Assert.Equal("rotated-secret", stored["secretAccessKey"]);
        }
    }

    [SqlFact]
    public async Task Put_RejectsAccountAndBucketNamesThatCouldBendTheAddress()
    {
        using (WebApplicationFactory<Program> host = WithR2AppSettings())
        {
            HttpClient client = await AdminClientAsync(host);
            List<(string Account, string Bucket)> bad = new List<(string, string)>
            {
                ("evil.com/x#", "bucket-one"),
                ("a.b", "bucket-one"),
                ("acc 1", "bucket-one"),
                ("../x", "bucket-one"),
                ("", "bucket-one"),
                ("acc1", "Bucket"),
                ("acc1", "a"),
                ("acc1", "a_b"),
                ("acc1", "-abc"),
                ("acc1", "bucket.name"),
                ("acc1", ""),
            };

            foreach ((string account, string bucket) in bad)
            {
                HttpResponseMessage response = await client.PutAsJsonAsync(
                    R2Url, Body(account, bucket, 0, confirmText: "R2"));

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }
    }

    [SqlFact]
    public async Task FirstSave_WithoutAnyKeys_Returns400OnTheMissingKey()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            R2Url, Body("acc1", "bucket-one", 0, confirmText: "R2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("secrets.accessKeyId", await response.Content.ReadAsStringAsync());
    }

    [SqlFact]
    public async Task FallbackSlot_IsNotSupported()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            BaseUrl + "/R2/FALLBACK", Body("acc1", "bucket-one", 0, "a", "b", "R2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlFact]
    public async Task Audit_NamesTheChangedFieldsButNeverTheirValues()
    {
        using (WebApplicationFactory<Program> host = WithR2AppSettings())
        {
            HttpClient client = await AdminClientAsync(host);
            await client.PutAsJsonAsync(R2Url, Body("acc1", "bucket-two", 0, "audit-access-LEAK", "audit-secret-LEAK", "R2"));

            await ApiTestHelpers.WithDbAsync(factory, async db =>
            {
                AuditLog log = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.SystemConfigUpdated);
                Assert.Contains("R2/PRIMARY", log.Reason);
                Assert.Contains("bucketName", log.Reason);
                Assert.DoesNotContain("bucket-two", log.Reason);
                Assert.DoesNotContain("LEAK", log.Reason);
            });
        }
    }
}
