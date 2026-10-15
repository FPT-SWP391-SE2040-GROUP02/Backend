using System.Net;
using System.Text.Json;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// API Owner xem nhật ký hoạt động của kho (SEC-13, SEC-04), trên SQL Server thật (database tạm).
/// </summary>
public sealed class AuditLogsApiTests : IAsyncLifetime
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

    private static string Url(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/audit-logs";
    }

    /// <summary>Owner có kho với 4 dòng nhật ký: tạo kho, tạo 2 gói, tạo 1 tài sản.</summary>
    private async Task<(HttpClient Client, Guid VaultId)> CreateVaultWithLogsAsync()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói A");
        await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói B");
        await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        return (client, vaultId);
    }

    [SqlFact]
    public async Task Sec13_Owner_SeesNewestFirstWithPaging()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await CreateVaultWithLogsAsync();

        // Hành động: trang 1, mỗi trang 3 dòng, mới nhất trước
        HttpResponseMessage response = await client.GetAsync($"{Url(vaultId)}?pageSize=3&isAscending=false");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        Assert.True(page.GetProperty("hasNextPage").GetBoolean());
        JsonElement items = page.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("ASSET_CREATED", items[0].GetProperty("action").GetString());
        Assert.Equal("OWNER", items[0].GetProperty("actorRole").GetString());
        Assert.Equal("ASSET", items[0].GetProperty("targetType").GetString());
        Assert.Equal("SUCCEEDED", items[0].GetProperty("outcome").GetString());
    }

    [SqlFact]
    public async Task Admin06_FilterByAction_ReturnsOnlyThatAction()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await CreateVaultWithLogsAsync();

        // Hành động
        HttpResponseMessage response = await client.GetAsync($"{Url(vaultId)}?action=CONTAINER_CREATED");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("CONTAINER_CREATED", item.GetProperty("action").GetString());
        }
    }

    [SqlFact]
    public async Task Admin06_FilterByTimeRange_ExcludesOutside()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await CreateVaultWithLogsAsync();
        string future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        string past = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));

        // Hành động: khoảng ở tương lai không có dòng nào; khoảng bao quanh hiện tại có đủ 4 dòng
        JsonElement none = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync($"{Url(vaultId)}?from={future}"));
        JsonElement all = await ApiTestHelpers.ReadJsonAsync(
            await client.GetAsync($"{Url(vaultId)}?from={past}&to={future}"));

        // Kiểm tra
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());
    }

    [SqlFact]
    public async Task Sec04_OtherUserVault_IsNotFound()
    {
        // Chuẩn bị: kho của người khác
        (HttpClient owner, Guid vaultId) = await CreateVaultWithLogsAsync();
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await stranger.GetAsync(Url(vaultId));

        // Kiểm tra: 404 giống hệt kho không tồn tại, không lộ nhật ký
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task Validation_BadActionSortOrRange_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await CreateVaultWithLogsAsync();
        string now = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));

        // Hành động và kiểm tra: action lạ, action dạng tên C#, sortBy lạ, from không trước to
        await ApiTestHelpers.AssertProblemAsync(
            await client.GetAsync($"{Url(vaultId)}?action=DELETE_EVERYTHING"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await client.GetAsync($"{Url(vaultId)}?action=ContainerCreated"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await client.GetAsync($"{Url(vaultId)}?sortBy=action"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await client.GetAsync($"{Url(vaultId)}?from={now}&to={now}"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }
}
