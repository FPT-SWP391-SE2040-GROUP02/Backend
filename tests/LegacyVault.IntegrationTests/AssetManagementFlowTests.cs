using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Owner tải, đổi tên và xóa tài sản qua HTTP trên SQL Server thật
/// (docs/api-contract.md mục 6.7, LV-10). Cách chạy giống ContainerFlowTests: [SqlFact] cần LEGACYVAULT_TEST_CONNECTION,
/// mỗi lớp test dùng một database tạm tên ngẫu nhiên và xóa khi xong.
/// </summary>
public sealed class AssetManagementFlowTests : IAsyncLifetime
{
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

    [SqlFact]
    public async Task OwnerDownloadsTextContentAndOthersCannot()
    {
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(owner);
        JsonElement asset = await CreateTextAsync(owner, vaultId, "Tài khoản", "user an / pass bí mật");

        HttpResponseMessage download = await owner.GetAsync(AssetUrl(vaultId, asset) + "/content");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("user an / pass bí mật", await download.Content.ReadAsStringAsync());
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
        Assert.Contains("Tài", Uri.UnescapeDataString(download.Content.Headers.ContentDisposition?.FileNameStar ?? string.Empty));

        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        await ApiTestHelpers.CreateVaultAsync(stranger);
        HttpResponseMessage denied = await stranger.GetAsync(AssetUrl(vaultId, asset) + "/content");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [SqlFact]
    public async Task RenameRequiresFreshEtag()
    {
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(owner);
        JsonElement asset = await CreateTextAsync(owner, vaultId, "Tên cũ", "nội dung");
        string etag = asset.GetProperty("etag").GetString()!;

        HttpResponseMessage renamed = await owner.PutAsJsonAsync(AssetUrl(vaultId, asset), new { name = "Tên mới", etag });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(renamed);
        Assert.Equal("Tên mới", after.GetProperty("name").GetString());
        Assert.NotEqual(etag, after.GetProperty("etag").GetString());
        Assert.False(after.TryGetProperty("currentVersion", out _));

        HttpResponseMessage stale = await owner.PutAsJsonAsync(AssetUrl(vaultId, asset), new { name = "Ghi đè", etag });
        await ApiTestHelpers.AssertProblemAsync(stale, HttpStatusCode.Conflict, "EDIT_CONFLICT");
    }

    [SqlFact]
    public async Task ContentCannotBeReplacedAndCountsQuotaOnce()
    {
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(owner);
        JsonElement asset = await CreateTextAsync(owner, vaultId, "Ghi chú", "bản một");
        string etag = asset.GetProperty("etag").GetString()!;

        // Không còn phiên bản: đường dẫn thêm phiên bản và tải theo ?version không còn tác dụng.
        HttpResponseMessage replaced = await owner.PostAsJsonAsync(
            AssetUrl(vaultId, asset) + "/versions/text", new { content = "bản hai dài hơn", etag });
        Assert.True(replaced.StatusCode == HttpStatusCode.NotFound || replaced.StatusCode == HttpStatusCode.MethodNotAllowed);
        Assert.Equal("bản một", await owner.GetStringAsync(AssetUrl(vaultId, asset) + "/content?version=9"));

        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await owner.GetAsync($"/api/v1/vaults/{vaultId}"));
        Assert.Equal(Encoding.UTF8.GetByteCount("bản một"), vault.GetProperty("storageUsedBytes").GetInt64());
    }

    [SqlFact]
    public async Task FileAssetKeepsFileNameAndContent()
    {
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(owner);

        byte[] bytes = Encoding.UTF8.GetBytes("pdf một");
        JsonElement fileAsset;
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            ByteArrayContent file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "giay-to.pdf");
            HttpResponseMessage created = await owner.PostAsync($"/api/v1/vaults/{vaultId}/assets", form);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            fileAsset = await ApiTestHelpers.ReadJsonAsync(created);
        }

        Assert.Equal("giay-to.pdf", fileAsset.GetProperty("fileName").GetString());
        Assert.Equal(bytes.Length, fileAsset.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(bytes, await owner.GetByteArrayAsync(AssetUrl(vaultId, fileAsset) + "/content"));
    }

    [SqlFact]
    public async Task DeleteIsBlockedWhileInContainerThenFreesQuota()
    {
        // Đưa tài sản vào gói cần hạng trả phí (SETUP-17).
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement asset = await CreateTextAsync(owner, vaultId, "Sẽ xóa", "nội dung xóa");
        Guid assetId = asset.GetProperty("id").GetGuid();
        JsonElement container = await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói A");
        Guid containerId = container.GetProperty("id").GetGuid();
        HttpResponseMessage added = await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetId);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        HttpResponseMessage blocked = await owner.DeleteAsync(AssetUrl(vaultId, asset));
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "ASSET_IN_USE");
        Assert.Equal("IN_CONTAINER", problem.GetProperty("reason").GetString());

        HttpResponseMessage removed = await owner.DeleteAsync(
            $"/api/v1/vaults/{vaultId}/containers/{containerId}/assets/{assetId}");
        Assert.True(removed.IsSuccessStatusCode);

        string[] before = Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories);
        Assert.Single(before);

        HttpResponseMessage deleted = await owner.DeleteAsync(AssetUrl(vaultId, asset));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        HttpResponseMessage gone = await owner.GetAsync(AssetUrl(vaultId, asset));
        await ApiTestHelpers.AssertProblemAsync(gone, HttpStatusCode.NotFound, "ASSET_NOT_FOUND");
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await owner.GetAsync($"/api/v1/vaults/{vaultId}"));
        Assert.Equal(0, vault.GetProperty("storageUsedBytes").GetInt64());
        Assert.Empty(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [SqlFact]
    public async Task EditsAreBlockedWhenPlanIsLocked()
    {
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(owner);
        JsonElement asset = await CreateTextAsync(owner, vaultId, "Khóa", "nội dung");
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.HandoverInProgress);

        HttpResponseMessage rename = await owner.PutAsJsonAsync(
            AssetUrl(vaultId, asset), new { name = "Đổi", etag = asset.GetProperty("etag").GetString() });
        await ApiTestHelpers.AssertProblemAsync(rename, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");

        HttpResponseMessage delete = await owner.DeleteAsync(AssetUrl(vaultId, asset));
        await ApiTestHelpers.AssertProblemAsync(delete, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
    }

    [SqlFact]
    public async Task DownloadWritesAuditWithoutAssetName()
    {
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(owner);
        JsonElement asset = await CreateTextAsync(owner, vaultId, "Tên bí mật cần giấu", "nội dung");
        await owner.GetStringAsync(AssetUrl(vaultId, asset) + "/content");

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<AuditLog> logs = await db.AuditLogs.Where(l => l.VaultId == vaultId).ToListAsync();
            Assert.Contains(logs, l => l.Action == AuditAction.AssetContentDownloaded);
            Assert.All(logs, l => Assert.DoesNotContain("giấu", l.Reason ?? string.Empty));
        });
    }

    private static string AssetUrl(Guid vaultId, JsonElement asset)
    {
        return $"/api/v1/vaults/{vaultId}/assets/{asset.GetProperty("id").GetGuid()}";
    }

    private static async Task<JsonElement> CreateTextAsync(HttpClient client, Guid vaultId, string name, string content)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/assets/text", new { name, content });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }
}
