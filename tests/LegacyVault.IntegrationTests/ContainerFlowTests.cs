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

    // ---------- Thêm / gỡ tài sản trong gói ----------

    [SqlFact]
    public async Task Ac04_Setup17_FreeVaultCannotAddAssetToContainer()
    {
        // Chuẩn bị: kho Free vẫn tạo được tài sản và gói.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage response = await AddAssetAsync(client, vaultId, containerId, assetId);

        // Kiểm tra: API chặn, gói vẫn rỗng.
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "PAID_TIER_REQUIRED");
        Assert.Equal(0, await CountLinksAsync(assetId));
    }

    [SqlFact]
    public async Task Setup04_PaidVaultAddsAssetToDraftContainer()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Sổ tiết kiệm");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();
        int versionBefore = await GetPlanConfigVersionAsync(client);

        // Hành động
        HttpResponseMessage response = await AddAssetAsync(client, vaultId, containerId, assetId);

        // Kiểm tra: chưa có người nhận nên vẫn DRAFT, chỉ còn thiếu NO_DESIGNATION; ConfigVersion tăng 1.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement container = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(new List<Guid> { assetId }, ReadGuids(container.GetProperty("assetIds")));
        Assert.Equal("DRAFT", container.GetProperty("status").GetString());
        Assert.Equal(new List<string> { "NO_DESIGNATION" }, ReadStrings(container.GetProperty("missingRequirements")));
        Assert.Equal(versionBefore + 1, await GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Setup03_AssetCannotJoinSecondContainer()
    {
        // Chuẩn bị: tài sản đã ở gói thứ nhất.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid firstId = (await CreateContainerAsync(client, vaultId, "Gói 1")).GetProperty("id").GetGuid();
        Guid secondId = (await CreateContainerAsync(client, vaultId, "Gói 2")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await AddAssetAsync(client, vaultId, firstId, assetId)).StatusCode);

        // Hành động
        HttpResponseMessage response = await AddAssetAsync(client, vaultId, secondId, assetId);

        // Kiểm tra: 409 chỉ ra gói đang giữ tài sản.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "ASSET_ALREADY_IN_CONTAINER");
        Assert.Equal(firstId, problem.GetProperty("containerId").GetGuid());
    }

    [SqlFact]
    public async Task Setup03_AddingSameAssetTwiceIsIdempotent()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();
        int versionBefore = await GetPlanConfigVersionAsync(client);

        // Hành động
        HttpResponseMessage first = await AddAssetAsync(client, vaultId, containerId, assetId);
        HttpResponseMessage second = await AddAssetAsync(client, vaultId, containerId, assetId);

        // Kiểm tra: cả 2 lần 200, cùng kết quả, DB chỉ 1 liên kết, ConfigVersion chỉ tăng 1.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);
        JsonElement secondBody = await ApiTestHelpers.ReadJsonAsync(second);
        Assert.Equal(firstBody.GetRawText(), secondBody.GetRawText());
        Assert.Equal(1, await CountLinksAsync(assetId));
        Assert.Equal(versionBefore + 1, await GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Sec04_AssetOfAnotherVaultIsNotFound()
    {
        // Chuẩn bị: người khác có một tài sản trong kho của họ.
        HttpClient other = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid otherVaultId = await CreateVaultAsync(other);
        Guid otherAssetId = await ApiTestHelpers.CreateTextAssetAsync(other, otherVaultId, "Của người khác");

        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage response = await AddAssetAsync(client, vaultId, containerId, otherAssetId);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "ASSET_NOT_FOUND");
        Assert.Equal(0, await CountLinksAsync(otherAssetId));
    }

    [SqlFact]
    public async Task Setup16_ArchivedContainerRejectsAddAndRemove()
    {
        // Chuẩn bị: một gói đã lưu trữ.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{ContainersUrl(vaultId)}/{containerId}")).StatusCode);

        // Hành động
        HttpResponseMessage add = await AddAssetAsync(client, vaultId, containerId, assetId);
        HttpResponseMessage remove = await client.DeleteAsync(ContainerAssetUrl(vaultId, containerId, assetId));

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(add, HttpStatusCode.Conflict, "CONTAINER_ARCHIVED");
        await ApiTestHelpers.AssertProblemAsync(remove, HttpStatusCode.Conflict, "CONTAINER_ARCHIVED");
    }

    [SqlFact]
    public async Task Quota02_RemovingAssetKeepsAssetAndUsedBytes()
    {
        // Chuẩn bị: gói có tài sản A; tài sản B không ở trong gói.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetA = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản A");
        Guid assetB = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản B");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();
        JsonElement withAsset = await ApiTestHelpers.ReadJsonAsync(await AddAssetAsync(client, vaultId, containerId, assetA));
        long usedBefore = await GetStorageUsedBytesAsync(client);
        int versionBefore = await GetPlanConfigVersionAsync(client);

        // Hành động: gỡ tài sản không có trong gói, rồi gỡ tài sản có trong gói.
        HttpResponseMessage removeMissing = await client.DeleteAsync(ContainerAssetUrl(vaultId, containerId, assetB));
        HttpResponseMessage removeReal = await client.DeleteAsync(ContainerAssetUrl(vaultId, containerId, assetA));

        // Kiểm tra: gỡ tài sản không có → 200, không đổi gì (etag và ConfigVersion giữ nguyên).
        Assert.Equal(HttpStatusCode.OK, removeMissing.StatusCode);
        JsonElement unchanged = await ApiTestHelpers.ReadJsonAsync(removeMissing);
        Assert.Equal(withAsset.GetRawText(), unchanged.GetRawText());

        // Gỡ thật → gói rỗng, ConfigVersion +1, tài sản vẫn còn và dung lượng đã dùng không đổi (QUOTA-02).
        Assert.Equal(HttpStatusCode.OK, removeReal.StatusCode);
        JsonElement removed = await ApiTestHelpers.ReadJsonAsync(removeReal);
        Assert.Equal(0, removed.GetProperty("assetIds").GetArrayLength());
        Assert.Equal(new List<string> { "NO_ASSET", "NO_DESIGNATION" }, ReadStrings(removed.GetProperty("missingRequirements")));
        Assert.Equal(versionBefore + 1, await GetPlanConfigVersionAsync(client));
        Assert.Equal(usedBefore, await GetStorageUsedBytesAsync(client));
        HttpResponseMessage asset = await client.GetAsync($"/api/v1/vaults/{vaultId}/assets/{assetA}");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
    }

    [SqlFact]
    public async Task Setup16_ArchivedContainerReleasesAssetForAnotherContainer()
    {
        // Chuẩn bị: tài sản ở gói 1, rồi lưu trữ gói 1.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid firstId = (await CreateContainerAsync(client, vaultId, "Gói 1")).GetProperty("id").GetGuid();
        Guid secondId = (await CreateContainerAsync(client, vaultId, "Gói 2")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await AddAssetAsync(client, vaultId, firstId, assetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{ContainersUrl(vaultId)}/{firstId}")).StatusCode);

        // Hành động
        HttpResponseMessage response = await AddAssetAsync(client, vaultId, secondId, assetId);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement second = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(new List<Guid> { assetId }, ReadGuids(second.GetProperty("assetIds")));
        Assert.Equal(1, await CountLinksAsync(assetId));
    }

    [SqlFact]
    public async Task Oplan01_GraceReadOnlyBlocksAddButAllowsRemove()
    {
        // Chuẩn bị: gói có tài sản A (thêm khi còn trả phí), rồi chuyển dịch vụ sang ân hạn.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetA = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản A");
        Guid assetB = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản B");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await AddAssetAsync(client, vaultId, containerId, assetA)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EntitlementStatus.GraceReadOnly));
        });

        // Hành động
        HttpResponseMessage add = await AddAssetAsync(client, vaultId, containerId, assetB);
        HttpResponseMessage remove = await client.DeleteAsync(ContainerAssetUrl(vaultId, containerId, assetA));

        // Kiểm tra: thêm là thao tác "thêm" → chặn; gỡ là thao tác "giảm" → cho.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(add, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("SERVICE_READ_ONLY", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(0, (await ApiTestHelpers.ReadJsonAsync(remove)).GetProperty("assetIds").GetArrayLength());
    }

    [SqlFact]
    public async Task Setup16_RemovingLastAssetOfLastReadyContainerNeedsConfirmation()
    {
        // Chuẩn bị: gói có 1 tài sản; chưa có module người nhận nên gói READY và kế hoạch ACTIVE dựng thẳng trong DB.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = (await CreateContainerAsync(client, vaultId, "Gói sẵn sàng")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await AddAssetAsync(client, vaultId, containerId, assetId)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Containers.Where(c => c.Id == containerId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContainerStatus.Ready));
            await db.HandoverPlans.Where(p => p.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PlanStatus.Active));
        });
        string url = ContainerAssetUrl(vaultId, containerId, assetId);

        // Hành động: gỡ khi chưa xác nhận.
        HttpResponseMessage unconfirmed = await client.DeleteAsync(url);

        // Kiểm tra: bị chặn, tài sản vẫn trong gói.
        await ApiTestHelpers.AssertProblemAsync(
            unconfirmed, HttpStatusCode.Conflict, "PLAN_DEACTIVATION_CONFIRMATION_REQUIRED");
        JsonElement stillThere = await ApiTestHelpers.ReadJsonAsync(
            await client.GetAsync($"{ContainersUrl(vaultId)}/{containerId}"));
        Assert.Equal(new List<Guid> { assetId }, ReadGuids(stillThere.GetProperty("assetIds")));
        Assert.Equal("READY", stillThere.GetProperty("status").GetString());

        // Hành động: gỡ có xác nhận.
        HttpResponseMessage confirmed = await client.DeleteAsync($"{url}?confirmPlanDeactivation=true");

        // Kiểm tra: gói về DRAFT, kế hoạch về DRAFT.
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        JsonElement container = await ApiTestHelpers.ReadJsonAsync(confirmed);
        Assert.Equal("DRAFT", container.GetProperty("status").GetString());
        Assert.Equal(0, container.GetProperty("assetIds").GetArrayLength());
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("DRAFT", vault.GetProperty("planStatus").GetString());
    }

    [SqlFact]
    public async Task Setup03_ConcurrentAddToTwoContainersKeepsAssetInOne()
    {
        // Chuẩn bị: hai gói, một tài sản.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid firstId = (await CreateContainerAsync(client, vaultId, "Gói 1")).GetProperty("id").GetGuid();
        Guid secondId = (await CreateContainerAsync(client, vaultId, "Gói 2")).GetProperty("id").GetGuid();

        // Hành động: gửi song song; nếu cả hai cùng qua bước kiểm tra liên kết thì unique AssetId chặn một request
        // (TryAddAssetLinkAsync trả false → service đọc lại và trả 409).
        Task<HttpResponseMessage> toFirst = AddAssetAsync(client, vaultId, firstId, assetId);
        Task<HttpResponseMessage> toSecond = AddAssetAsync(client, vaultId, secondId, assetId);
        HttpResponseMessage[] responses = await Task.WhenAll(toFirst, toSecond);

        // Kiểm tra: đúng một 200 và một 409 trỏ tới gói thắng; DB chỉ 1 liên kết.
        // Lưu ý: nếu sau này HandoverPlan.ConfigVersion thành concurrency token thì request thua có thể nhận
        // 409 EDIT_CONFLICT (hai request cùng sửa Plan) thay vì ASSET_ALREADY_IN_CONTAINER; khi đó cần chấp nhận cả hai mã.
        HttpResponseMessage winner;
        HttpResponseMessage loser;
        Guid winnerContainerId;
        if (responses[0].StatusCode == HttpStatusCode.OK)
        {
            winner = responses[0];
            loser = responses[1];
            winnerContainerId = firstId;
        }
        else
        {
            winner = responses[1];
            loser = responses[0];
            winnerContainerId = secondId;
        }
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            loser, HttpStatusCode.Conflict, "ASSET_ALREADY_IN_CONTAINER");
        Assert.Equal(winnerContainerId, problem.GetProperty("containerId").GetGuid());
        Assert.Equal(1, await CountLinksAsync(assetId));
    }

    private static string ContainersUrl(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/containers";
    }

    private static string ContainerAssetUrl(Guid vaultId, Guid containerId, Guid assetId)
    {
        return $"{ContainersUrl(vaultId)}/{containerId}/assets/{assetId}";
    }

    private static Task<HttpResponseMessage> AddAssetAsync(HttpClient client, Guid vaultId, Guid containerId, Guid assetId)
    {
        return client.PostAsJsonAsync($"{ContainersUrl(vaultId)}/{containerId}/assets", new { assetId });
    }

    /// <summary>Số dòng ContainerAssets của một tài sản (đọc thẳng DB).</summary>
    private async Task<int> CountLinksAsync(Guid assetId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.ContainerAssets.CountAsync(l => l.AssetId == assetId);
        });
        return count;
    }

    private static async Task<long> GetStorageUsedBytesAsync(HttpClient client)
    {
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        return vault.GetProperty("storageUsedBytes").GetInt64();
    }

    private static List<Guid> ReadGuids(JsonElement array)
    {
        List<Guid> values = new List<Guid>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            values.Add(item.GetGuid());
        }
        return values;
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
