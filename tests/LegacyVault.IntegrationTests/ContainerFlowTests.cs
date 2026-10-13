using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// CRUD gói (Container) qua HTTP trên SQL Server thật (docs/api-contract.md mục 6.6).
/// </summary>
/// <remarks>
/// Cách chạy:
/// - [SqlFact] chỉ chạy khi có biến môi trường LEGACYVAULT_TEST_CONNECTION (connection string tới SQL Server,
///   xem README mục 8); không có biến thì test hiện Skipped.
/// - Mỗi lớp test (InitializeAsync) tạo một ApiFactory: chạy API trong bộ nhớ, trỏ tới một database TẠM tên ngẫu nhiên,
///   áp migration; DisposeAsync xóa database đó. Không đụng database dev.
/// - Đăng nhập dùng header X-Dev-User-Id của môi trường Development; mỗi test tự tạo user riêng nên không ảnh hưởng nhau.
/// - Dữ liệu khó tạo qua API (50 gói, kho đang ân hạn, gói READY) thì sửa thẳng DB ở phần "Chuẩn bị".
/// </remarks>
public sealed class ContainerFlowTests : IAsyncLifetime
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
    public async Task Setup17_FreeVaultCanCreateEmptyDraftContainer()
    {
        // Chuẩn bị: kho mới luôn ở hạng Free.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(ContainersUrl(vaultId),
            new { name = "  Tài liệu gia đình  ", description = "Giấy tờ nhà" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        JsonElement container = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(vaultId, container.GetProperty("vaultId").GetGuid());
        Assert.Equal("Tài liệu gia đình", container.GetProperty("name").GetString());
        Assert.Equal("Giấy tờ nhà", container.GetProperty("description").GetString());
        Assert.Equal("DRAFT", container.GetProperty("status").GetString());
        Assert.Equal(0, container.GetProperty("assetIds").GetArrayLength());
        Assert.Equal(new List<string> { "NO_ASSET", "NO_DESIGNATION" }, ReadStrings(container.GetProperty("missingRequirements")));
        Assert.Equal(JsonValueKind.Null, container.GetProperty("updatedAt").ValueKind);
        Assert.Equal("\"0\"", container.GetProperty("etag").GetString());
    }

    [SqlFact]
    public async Task Nfr14_FiftyFirstActiveContainerIsRejected()
    {
        // Chuẩn bị: thêm thẳng 50 gói chưa lưu trữ vào DB.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            for (int i = 1; i <= 50; i++)
            {
                Container container = new Container();
                container.VaultId = vaultId;
                container.Name = $"Gói {i}";
                container.Status = ContainerStatus.Draft;
                container.CreatedAt = DateTimeOffset.UtcNow;
                db.Containers.Add(container);
            }
            await db.SaveChangesAsync();
        });

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(ContainersUrl(vaultId), new { name = "Gói 51" });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "CONTAINER_LIMIT_EXCEEDED");
        Assert.Equal(50, problem.GetProperty("maxContainers").GetInt32());
    }

    [SqlFact]
    public async Task Sec04_ContainerOfAnotherVaultIsHidden()
    {
        // Chuẩn bị: chủ kho A có một gói; người lạ có kho B riêng.
        HttpClient owner = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid ownerVaultId = await CreateVaultAsync(owner);
        JsonElement container = await CreateContainerAsync(owner, ownerVaultId, "Gói của chủ kho");
        Guid containerId = container.GetProperty("id").GetGuid();
        string? etag = container.GetProperty("etag").GetString();

        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid strangerVaultId = await CreateVaultAsync(stranger);
        string viaOwnVault = $"{ContainersUrl(strangerVaultId)}/{containerId}";
        string viaOwnerVault = $"{ContainersUrl(ownerVaultId)}/{containerId}";

        // Hành động: người lạ xem / sửa / lưu trữ gói qua kho của mình và qua kho của chủ.
        HttpResponseMessage getOwn = await stranger.GetAsync(viaOwnVault);
        HttpResponseMessage putOwn = await stranger.PutAsJsonAsync(viaOwnVault, new { etag, name = "Chiếm" });
        HttpResponseMessage deleteOwn = await stranger.DeleteAsync(viaOwnVault);
        HttpResponseMessage getOther = await stranger.GetAsync(viaOwnerVault);
        HttpResponseMessage deleteOther = await stranger.DeleteAsync(viaOwnerVault);

        // Kiểm tra: gói kho khác → CONTAINER_NOT_FOUND; kho người khác → VAULT_NOT_FOUND; gói của chủ không đổi.
        await ApiTestHelpers.AssertProblemAsync(getOwn, HttpStatusCode.NotFound, "CONTAINER_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(putOwn, HttpStatusCode.NotFound, "CONTAINER_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(deleteOwn, HttpStatusCode.NotFound, "CONTAINER_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(getOther, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(deleteOther, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");

        JsonElement unchanged = await ApiTestHelpers.ReadJsonAsync(await owner.GetAsync(viaOwnerVault));
        Assert.Equal("Gói của chủ kho", unchanged.GetProperty("name").GetString());
        Assert.Equal("DRAFT", unchanged.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Setup04_UpdateRejectsStaleEtagAndNoChangeKeepsConfigVersion()
    {
        // Chuẩn bị: một gói đã đổi tên một lần (etag ban đầu thành cũ).
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        JsonElement container = await CreateContainerAsync(client, vaultId, "Tên cũ");
        string url = $"{ContainersUrl(vaultId)}/{container.GetProperty("id").GetGuid()}";
        string? oldEtag = container.GetProperty("etag").GetString();

        HttpResponseMessage renamed = await client.PutAsJsonAsync(url, new { etag = oldEtag, name = "Tên mới" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        JsonElement current = await ApiTestHelpers.ReadJsonAsync(renamed);
        string? currentEtag = current.GetProperty("etag").GetString();
        int versionBefore = await GetPlanConfigVersionAsync(client);

        // Hành động
        HttpResponseMessage stale = await client.PutAsJsonAsync(url, new { etag = oldEtag, name = "Ghi đè" });
        HttpResponseMessage noChange = await client.PutAsJsonAsync(url, new { etag = currentEtag, name = "  Tên mới  " });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(stale, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode);
        JsonElement afterNoChange = await ApiTestHelpers.ReadJsonAsync(noChange);
        Assert.Equal("Tên mới", afterNoChange.GetProperty("name").GetString());
        Assert.Equal(currentEtag, afterNoChange.GetProperty("etag").GetString());
        Assert.Equal(versionBefore, await GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Setup16_ArchivedContainerIsReadOnlyAndArchiveIsIdempotent()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        JsonElement container = await CreateContainerAsync(client, vaultId, "Gói sẽ lưu trữ");
        string url = $"{ContainersUrl(vaultId)}/{container.GetProperty("id").GetGuid()}";

        // Hành động: lưu trữ 2 lần rồi thử sửa.
        HttpResponseMessage first = await client.DeleteAsync(url);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);
        int versionAfterFirst = await GetPlanConfigVersionAsync(client);
        HttpResponseMessage second = await client.DeleteAsync(url);
        JsonElement secondBody = await ApiTestHelpers.ReadJsonAsync(second);
        HttpResponseMessage update = await client.PutAsJsonAsync(url,
            new { etag = secondBody.GetProperty("etag").GetString(), name = "Sửa gói đã lưu trữ" });

        // Kiểm tra: cả 2 lần 200, cùng kết quả, lần 2 không tăng ConfigVersion; sửa → CONTAINER_ARCHIVED.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("ARCHIVED", firstBody.GetProperty("status").GetString());
        Assert.Equal(0, firstBody.GetProperty("missingRequirements").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, firstBody.GetProperty("archivedAt").ValueKind);
        Assert.Equal(firstBody.GetRawText(), secondBody.GetRawText());
        Assert.Equal(versionAfterFirst, await GetPlanConfigVersionAsync(client));
        await ApiTestHelpers.AssertProblemAsync(update, HttpStatusCode.Conflict, "CONTAINER_ARCHIVED");
    }

    [SqlFact]
    public async Task Setup16_ListHidesArchivedByDefaultAndRejectsUnknownSort()
    {
        // Chuẩn bị: hai gói, lưu trữ một gói.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await CreateContainerAsync(client, vaultId, "B - còn dùng");
        JsonElement archived = await CreateContainerAsync(client, vaultId, "A - đã lưu trữ");
        Guid archivedId = archived.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{ContainersUrl(vaultId)}/{archivedId}")).StatusCode);

        // Hành động
        HttpResponseMessage defaultList = await client.GetAsync(ContainersUrl(vaultId));
        HttpResponseMessage withArchived = await client.GetAsync($"{ContainersUrl(vaultId)}?includeArchived=true&sortBy=name");
        HttpResponseMessage badSort = await client.GetAsync($"{ContainersUrl(vaultId)}?sortBy=status");

        // Kiểm tra
        JsonElement defaultPage = await ApiTestHelpers.ReadJsonAsync(defaultList);
        Assert.Equal(1, defaultPage.GetProperty("totalCount").GetInt32());
        Assert.Equal("B - còn dùng", defaultPage.GetProperty("items")[0].GetProperty("name").GetString());

        JsonElement fullPage = await ApiTestHelpers.ReadJsonAsync(withArchived);
        Assert.Equal(2, fullPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(archivedId, fullPage.GetProperty("items")[0].GetProperty("id").GetGuid());

        await ApiTestHelpers.AssertProblemAsync(badSort, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Setup04_CreateRenameArchiveEachBumpConfigVersionByOne()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        int start = await GetPlanConfigVersionAsync(client);

        // Hành động + Kiểm tra: mỗi bước tăng đúng 1.
        JsonElement container = await CreateContainerAsync(client, vaultId, "Gói");
        Assert.Equal(start + 1, await GetPlanConfigVersionAsync(client));

        string url = $"{ContainersUrl(vaultId)}/{container.GetProperty("id").GetGuid()}";
        HttpResponseMessage renamed = await client.PutAsJsonAsync(url,
            new { etag = container.GetProperty("etag").GetString(), name = "Gói đổi tên" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(start + 2, await GetPlanConfigVersionAsync(client));

        HttpResponseMessage archived = await client.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(start + 3, await GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Oplan01_GraceReadOnlyBlocksCreateButAllowsArchive()
    {
        // Chuẩn bị: có sẵn một gói, rồi chuyển dịch vụ sang ân hạn chỉ đọc.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        JsonElement container = await CreateContainerAsync(client, vaultId, "Gói có sẵn");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EntitlementStatus.GraceReadOnly));
        });

        // Hành động
        HttpResponseMessage create = await client.PostAsJsonAsync(ContainersUrl(vaultId), new { name = "Gói mới" });
        HttpResponseMessage archive = await client.DeleteAsync(
            $"{ContainersUrl(vaultId)}/{container.GetProperty("id").GetGuid()}");

        // Kiểm tra: tạo là thao tác "thêm" → chặn; lưu trữ là thao tác "giảm" → cho.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            create, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("SERVICE_READ_ONLY", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Equal("ARCHIVED", (await ApiTestHelpers.ReadJsonAsync(archive)).GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Setup16_ArchivingLastReadyContainerOfRunningPlanNeedsConfirmation()
    {
        // Chuẩn bị: chưa có module người nhận nên gói READY và kế hoạch ACTIVE được dựng thẳng trong DB;
        // gói có một tài sản để kiểm tài sản được gỡ ra nhưng không bị xóa.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        JsonElement container = await CreateContainerAsync(client, vaultId, "Gói sẵn sàng");
        Guid containerId = container.GetProperty("id").GetGuid();
        HttpResponseMessage assetResponse = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/assets/text", new { name = "Ghi chú", content = "nội dung" });
        Guid assetId = (await ApiTestHelpers.ReadJsonAsync(assetResponse)).GetProperty("id").GetGuid();

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            ContainerAsset link = new ContainerAsset();
            link.ContainerId = containerId;
            link.AssetId = assetId;
            link.AddedAt = DateTimeOffset.UtcNow;
            db.ContainerAssets.Add(link);
            await db.SaveChangesAsync();
            await db.Containers.Where(c => c.Id == containerId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContainerStatus.Ready));
            await db.HandoverPlans.Where(p => p.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PlanStatus.Active));
        });
        string url = $"{ContainersUrl(vaultId)}/{containerId}";

        // Hành động: lưu trữ khi chưa xác nhận, rồi xác nhận.
        HttpResponseMessage unconfirmed = await client.DeleteAsync(url);
        HttpResponseMessage confirmed = await client.DeleteAsync($"{url}?confirmPlanDeactivation=true");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(
            unconfirmed, HttpStatusCode.Conflict, "PLAN_DEACTIVATION_CONFIRMATION_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        JsonElement archived = await ApiTestHelpers.ReadJsonAsync(confirmed);
        Assert.Equal("ARCHIVED", archived.GetProperty("status").GetString());
        Assert.Equal(0, archived.GetProperty("assetIds").GetArrayLength());

        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("DRAFT", vault.GetProperty("planStatus").GetString());
        HttpResponseMessage asset = await client.GetAsync($"/api/v1/vaults/{vaultId}/assets/{assetId}");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
    }

    private static string ContainersUrl(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/containers";
    }

    private static async Task<Guid> CreateVaultAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync("/api/v1/vaults", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(response);
        return vault.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateContainerAsync(HttpClient client, Guid vaultId, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(ContainersUrl(vaultId), new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private static async Task<int> GetPlanConfigVersionAsync(HttpClient client)
    {
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        return vault.GetProperty("planConfigVersion").GetInt32();
    }

    private static List<string> ReadStrings(JsonElement array)
    {
        List<string> values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            string? value = item.GetString();
            if (value != null)
            {
                values.Add(value);
            }
        }
        return values;
    }
}
