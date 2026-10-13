using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Luồng tạo kho → tạo tài sản trên SQL Server thật. Mỗi lần chạy dùng một database tạm riêng
/// (áp migration, xóa khi xong), không đụng database dev.
/// </summary>
public sealed class VaultAssetFlowTests : IAsyncLifetime
{
    private const string DevUserHeader = "X-Dev-User-Id";
    private ApiFactory factory = null!;
    private string storageRoot = null!;

    public async Task InitializeAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable("LEGACYVAULT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            return;
        }
        var connection = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"LegacyVault_Test_{Guid.NewGuid():N}",
        }.ConnectionString;
        storageRoot = Path.Combine(Path.GetTempPath(), $"legacyvault-test-{Guid.NewGuid():N}");
        factory = new ApiFactory(connection, storageRoot);

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is null)
        {
            return;
        }
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await factory.DisposeAsync();
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [SqlFact]
    public async Task CreateVaultIsIdempotentAndStartsFree()
    {
        var client = await ClientForNewUserAsync(emailVerified: true);

        var missing = await client.GetAsync("/api/v1/vaults/me");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");

        var created = await client.PostAsJsonAsync("/api/v1/vaults", new { name = "  Kho gia đình  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var vault = await ReadJsonAsync(created);
        Assert.Equal("Kho gia đình", vault.GetProperty("name").GetString());
        Assert.Equal("OPEN", vault.GetProperty("status").GetString());
        Assert.Equal("DRAFT", vault.GetProperty("planStatus").GetString());
        Assert.Equal(1, vault.GetProperty("planConfigVersion").GetInt32());
        Assert.Equal("FREE", vault.GetProperty("serviceTier").GetString());
        Assert.Equal(20 * 1_048_576L, vault.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(0, vault.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal("\"0\"", vault.GetProperty("etag").GetString());

        // Bấm lặp và gửi song song vẫn chỉ có một kho (AC-89).
        var repeats = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsync("/api/v1/vaults", null)));
        foreach (var repeat in repeats)
        {
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
            Assert.Equal(vault.GetProperty("id").GetGuid(), (await ReadJsonAsync(repeat)).GetProperty("id").GetGuid());
        }
    }

    [SqlFact]
    public async Task ConcurrentFirstCreateYieldsSingleVault()
    {
        var client = await ClientForNewUserAsync(emailVerified: true);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsync("/api/v1/vaults", null)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        var ids = await Task.WhenAll(responses.Select(async r => (await ReadJsonAsync(r)).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
    }

    [SqlFact]
    public async Task UnverifiedEmailCannotCreateVault()
    {
        var client = await ClientForNewUserAsync(emailVerified: false);
        var response = await client.PostAsync("/api/v1/vaults", null);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");
    }

    [SqlFact]
    public async Task UpdateVaultRejectsStaleEtag()
    {
        var client = await ClientForNewUserAsync(emailVerified: true);
        var vault = await ReadJsonAsync(await client.PostAsync("/api/v1/vaults", null));
        var url = $"/api/v1/vaults/{vault.GetProperty("id").GetGuid()}";
        var etag = vault.GetProperty("etag").GetString();

        var updated = await client.PutAsJsonAsync(url, new { etag, name = "Tên mới" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, (await ReadJsonAsync(updated)).GetProperty("etag").GetString());

        var stale = await client.PutAsJsonAsync(url, new { etag, name = "Ghi đè" });
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "EDIT_CONFLICT");
    }

    [SqlFact]
    public async Task CreateFileAndTextAssetsCountTowardsQuota()
    {
        var client = await ClientForNewUserAsync(emailVerified: true);
        var vaultId = (await ReadJsonAsync(await client.PostAsync("/api/v1/vaults", null))).GetProperty("id").GetGuid();
        var assetsUrl = $"/api/v1/vaults/{vaultId}/assets";

        var bytes = new byte[1500];
        Random.Shared.NextBytes(bytes);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "C:\\fakepath\\giay-to.pdf");
        form.Add(new StringContent("Giấy tờ nhà"), "name");

        var fileResponse = await client.PostAsync(assetsUrl, form);
        Assert.Equal(HttpStatusCode.Created, fileResponse.StatusCode);
        var fileAsset = await ReadJsonAsync(fileResponse);
        Assert.Equal("FILE", fileAsset.GetProperty("kind").GetString());
        Assert.Equal("Giấy tờ nhà", fileAsset.GetProperty("name").GetString());
        var version = fileAsset.GetProperty("currentVersion");
        Assert.Equal(1, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal("giay-to.pdf", version.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", version.GetProperty("contentType").GetString());
        Assert.Equal(1500, version.GetProperty("sizeBytes").GetInt64());
        Assert.False(version.TryGetProperty("storageKey", out _));

        var textResponse = await client.PostAsJsonAsync($"{assetsUrl}/text",
            new { name = "Tài khoản ngân hàng", content = "user: an / pass: bí mật" });
        Assert.Equal(HttpStatusCode.Created, textResponse.StatusCode);
        var textAsset = await ReadJsonAsync(textResponse);
        Assert.Equal("TEXT", textAsset.GetProperty("kind").GetString());
        Assert.Null(textAsset.GetProperty("currentVersion").GetProperty("fileName").GetString());

        var vault = await ReadJsonAsync(await client.GetAsync($"/api/v1/vaults/{vaultId}"));
        var textBytes = System.Text.Encoding.UTF8.GetByteCount("user: an / pass: bí mật");
        Assert.Equal(1500 + textBytes, vault.GetProperty("storageUsedBytes").GetInt64());

        var page = await ReadJsonAsync(await client.GetAsync($"{assetsUrl}?sortBy=name&pageSize=1"));
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
        Assert.Equal("Giấy tờ nhà", page.GetProperty("items")[0].GetProperty("name").GetString());

        var badSort = await client.GetAsync($"{assetsUrl}?sortBy=storageKey");
        await AssertProblemAsync(badSort, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var detail = await client.GetAsync($"{assetsUrl}/{fileAsset.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        string[] storedFiles = Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories);
        Assert.Equal(2, storedFiles.Length);

        // Nội dung trên đĩa đã mã hóa; DB giữ DEK đã bọc + id KEK, không giữ DEK gốc.
        byte[] secret = System.Text.Encoding.UTF8.GetBytes("user: an / pass: bí mật");
        foreach (string path in storedFiles)
        {
            Assert.Equal(-1, File.ReadAllBytes(path).AsSpan().IndexOf(secret));
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var versions = await db.Set<AssetVersion>().Where(v => v.Asset.VaultId == vaultId).ToListAsync();
            Assert.Equal(2, versions.Count);
            Assert.All(versions, v => Assert.Equal("dev-local", v.EncryptionKeyId));
            Assert.All(versions, v => Assert.Equal(60, v.WrappedDataKey.Length));
        }
    }

    [SqlFact]
    public async Task UploadOverQuotaIsRejectedAndLeavesNoContent()
    {
        var client = await ClientForNewUserAsync(emailVerified: true);
        var vaultId = (await ReadJsonAsync(await client.PostAsync("/api/v1/vaults", null))).GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[20 * 1_048_576 + 1]), "file", "lon.bin");
        var response = await client.PostAsync($"/api/v1/vaults/{vaultId}/assets", form);

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "STORAGE_QUOTA_EXCEEDED");
        Assert.Equal(20 * 1_048_576L, problem.GetProperty("storageRemainingBytes").GetInt64());
        Assert.False(Directory.Exists(storageRoot)
            && Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories).Length > 0);
    }

    [SqlFact]
    public async Task OtherUsersVaultAndAssetsAreHidden()
    {
        var owner = await ClientForNewUserAsync(emailVerified: true);
        var vaultId = (await ReadJsonAsync(await owner.PostAsync("/api/v1/vaults", null))).GetProperty("id").GetGuid();
        var asset = await ReadJsonAsync(await owner.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/assets/text", new { name = "Ghi chú", content = "bí mật" }));

        var stranger = await ClientForNewUserAsync(emailVerified: true);
        await AssertProblemAsync(await stranger.GetAsync($"/api/v1/vaults/{vaultId}"),
            HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await AssertProblemAsync(
            await stranger.GetAsync($"/api/v1/vaults/{vaultId}/assets/{asset.GetProperty("id").GetGuid()}"),
            HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await AssertProblemAsync(
            await stranger.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/assets/text", new { name = "x", content = "y" }),
            HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task ClosedVaultRejectsNewAssets()
    {
        var client = await ClientForNewUserAsync(emailVerified: true);
        var vaultId = (await ReadJsonAsync(await client.PostAsync("/api/v1/vaults", null))).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Vaults.Where(v => v.Id == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VaultStatus.Closed));
        }

        var response = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/assets/text", new { name = "Ghi chú", content = "nội dung" });
        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("VAULT_CLOSED", problem.GetProperty("reason").GetString());
    }

    private async Task<HttpClient> ClientForNewUserAsync(bool emailVerified)
    {
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@test.local",
            FullName = "Người thử",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (emailVerified)
        {
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevUserHeader, user.Id.ToString());
        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private sealed class ApiFactory(string connectionString, string storageRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = connectionString,
                    ["AssetStorage:RootPath"] = storageRoot,
                }));
        }
    }
}

/// <summary>Kiểm tra không cần SQL: xác thực và quy đổi tên enum.</summary>
public sealed class VaultApiWithoutDatabaseTests
{
    [Fact]
    public async Task MissingDevUserHeaderReturnsSessionExpired()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] =
                        "Server=127.0.0.1,1;Database=Unavailable;User Id=sa;Password=TestOnly123!;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1",
                }));
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/vaults/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SESSION_EXPIRED", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(EntitlementStatus.PaidActive, "PAID_ACTIVE")]
    [InlineData(ServiceTier.XsMax, "XS_MAX")]
    [InlineData(PlanStatus.CheckinOverdue, "CHECKIN_OVERDUE")]
    [InlineData(AssetKind.File, "FILE")]
    public void EnumNamesMapToUpperSnakeCase(object value, string expected)
    {
        Assert.Equal(expected, EnumText.ToUpperSnake(value.ToString()!));
    }
}
