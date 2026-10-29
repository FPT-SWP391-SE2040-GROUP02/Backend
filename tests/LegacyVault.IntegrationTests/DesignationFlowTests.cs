using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Chỉ định người nhận cho gói, đếm suất, gói tự chuyển READY qua HTTP trên SQL Server thật
/// (docs/api-contract.md mục 6.5, 6.6, 6.8).
/// </summary>
/// <remarks>
/// Cách chạy giống ContainerFlowTests: [SqlFact] chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION; mỗi lớp test dùng
/// một database tạm. Kho XS (UpgradeVaultToXsAsync) có 2 suất người nhận. Dữ liệu khó tạo qua API
/// (hạng, suất mua thêm, kế hoạch ACTIVE, ân hạn) thì sửa thẳng DB ở phần "Chuẩn bị".
/// </remarks>
public sealed class DesignationFlowTests : IAsyncLifetime
{
    private const string CitizenA = "001090000001";
    private const string CitizenB = "001090000002";
    private const string CitizenC = "001090000003";

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
    public async Task Ac04_Setup17_FreeVaultCannotAddBeneficiaryToContainer()
    {
        // Chuẩn bị: tạo hồ sơ khi kho còn trả phí, rồi đưa kho về Free.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid beneficiaryId = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Tier, ServiceTier.Free)
                    .SetProperty(e => e.Status, EntitlementStatus.Free));
        });

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "PAID_TIER_REQUIRED");
        Assert.Equal(0, await CountDesignationsAsync(containerId));
    }

    [SqlFact]
    public async Task Sec04_BeneficiaryOfAnotherVaultIsNotFound()
    {
        // Chuẩn bị: hồ sơ thuộc kho khác.
        (HttpClient other, Guid otherVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid otherBeneficiaryId = await CreateBeneficiaryIdAsync(other, otherVaultId, CitizenA, "Người A");
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, otherBeneficiaryId);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "BENEFICIARY_NOT_FOUND");
    }

    [SqlFact]
    public async Task Setup16_ArchivedContainerRejectsBeneficiary()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid beneficiaryId = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(ContainerUrl(vaultId, containerId))).StatusCode);

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "CONTAINER_ARCHIVED");
    }

    [SqlFact]
    public async Task Setup04_AddingSameBeneficiaryTwiceIsIdempotent()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid beneficiaryId = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        int versionBefore = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động
        HttpResponseMessage first = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId);
        HttpResponseMessage second = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        JsonElement container = await ApiTestHelpers.ReadJsonAsync(second);
        Assert.Equal(new List<Guid> { beneficiaryId }, ReadGuids(container.GetProperty("beneficiaryIds")));
        Assert.Equal(1, await CountDesignationsAsync(containerId));
        Assert.Equal(versionBefore + 1, await ApiTestHelpers.GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Setup16_ReadyNeedsAssetAndBeneficiary()
    {
        // Chuẩn bị: ba gói — đủ cả hai, chỉ người nhận, chỉ tài sản.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid beneficiaryId = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid assetFull = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản 1");
        Guid assetOnly = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản 2");
        Guid fullId = await CreateContainerIdAsync(client, vaultId, "Đủ");
        Guid beneficiaryOnlyId = await CreateContainerIdAsync(client, vaultId, "Chỉ người nhận");
        Guid assetOnlyId = await CreateContainerIdAsync(client, vaultId, "Chỉ tài sản");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, fullId, assetFull)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, assetOnlyId, assetOnly)).StatusCode);

        // Hành động
        HttpResponseMessage full = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, fullId, beneficiaryId);
        HttpResponseMessage beneficiaryOnly = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, beneficiaryOnlyId, beneficiaryId);
        JsonElement assetOnlyContainer = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(ContainerUrl(vaultId, assetOnlyId)));

        // Kiểm tra
        JsonElement fullContainer = await ApiTestHelpers.ReadJsonAsync(full);
        Assert.Equal("READY", fullContainer.GetProperty("status").GetString());
        Assert.Equal(0, fullContainer.GetProperty("missingRequirements").GetArrayLength());

        JsonElement beneficiaryOnlyContainer = await ApiTestHelpers.ReadJsonAsync(beneficiaryOnly);
        Assert.Equal("DRAFT", beneficiaryOnlyContainer.GetProperty("status").GetString());
        Assert.Equal(new List<string> { "NO_ASSET" }, ReadStrings(beneficiaryOnlyContainer.GetProperty("missingRequirements")));

        Assert.Equal("DRAFT", assetOnlyContainer.GetProperty("status").GetString());
        Assert.Equal(new List<string> { "NO_DESIGNATION" }, ReadStrings(assetOnlyContainer.GetProperty("missingRequirements")));
    }

    [SqlFact]
    public async Task Ac92_Quota03_SlotsAreCountedPerPersonNotPerDesignation()
    {
        // Chuẩn bị: XS có 2 suất; A ở 3 gói, B ở 1 gói; gói thứ 4 còn trống.
        // Hồ sơ C tạo khi kho tạm có 3 suất rồi giảm về 2 (như kho bị giảm suất), vì tạo hồ sơ cũng bị giới hạn theo suất.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 1);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid c = await CreateBeneficiaryIdAsync(client, vaultId, CitizenC, "Người C");
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 0);
        List<Guid> containers = new List<Guid>();
        for (int i = 1; i <= 4; i++)
        {
            containers.Add(await CreateContainerIdAsync(client, vaultId, $"Gói {i}"));
        }
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containers[i], a)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containers[0], b)).StatusCode);

        // Hành động
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        HttpResponseMessage addC = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containers[3], c);
        HttpResponseMessage addAToFourth = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containers[3], a);

        // Kiểm tra
        Assert.Equal(2, vault.GetProperty("usedBeneficiarySlots").GetInt32());
        Assert.Equal(2, vault.GetProperty("includedBeneficiarySlots").GetInt32());
        Assert.Equal(0, vault.GetProperty("extraBeneficiarySlots").GetInt32());
        Assert.Equal(2, vault.GetProperty("beneficiarySlots").GetInt32());
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(addC, HttpStatusCode.Conflict, "BENEFICIARY_SLOTS_EXCEEDED");
        Assert.Equal(2, problem.GetProperty("usedSlots").GetInt32());
        Assert.Equal(2, problem.GetProperty("maxSlots").GetInt32());
        Assert.Equal(HttpStatusCode.OK, addAToFourth.StatusCode);
    }

    [SqlFact]
    public async Task Ac103_RemovingFromOneContainerKeepsSlotUntilRemovedEverywhere()
    {
        // Chuẩn bị: A ở 2 gói, B ở 1 gói → dùng hết 2 suất.
        // Hồ sơ C tạo khi kho tạm có 3 suất rồi giảm về 2 (như kho bị giảm suất), vì tạo hồ sơ cũng bị giới hạn theo suất.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 1);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid c = await CreateBeneficiaryIdAsync(client, vaultId, CitizenC, "Người C");
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 0);
        Guid first = await CreateContainerIdAsync(client, vaultId, "Gói 1");
        Guid second = await CreateContainerIdAsync(client, vaultId, "Gói 2");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, first, a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, second, a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, first, b)).StatusCode);

        // Hành động: gỡ A khỏi một gói.
        HttpResponseMessage removeOne = await client.DeleteAsync(ContainerBeneficiaryUrl(vaultId, first, a));
        int usedAfterOne = await GetUsedSlotsAsync(client);

        // Kiểm tra: A vẫn chiếm suất.
        Assert.Equal(HttpStatusCode.OK, removeOne.StatusCode);
        Assert.Equal(2, usedAfterOne);

        // Hành động: gỡ A khỏi gói còn lại, rồi gắn C.
        HttpResponseMessage removeAll = await client.DeleteAsync(ContainerBeneficiaryUrl(vaultId, second, a));
        int usedAfterAll = await GetUsedSlotsAsync(client);
        HttpResponseMessage addC = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, second, c);

        // Kiểm tra: A trả suất, C gắn được.
        Assert.Equal(HttpStatusCode.OK, removeAll.StatusCode);
        Assert.Equal(1, usedAfterAll);
        Assert.Equal(HttpStatusCode.OK, addC.StatusCode);
        Assert.Equal(2, await GetUsedSlotsAsync(client));
    }

    [SqlFact]
    public async Task Quota05_ExtraSlotAllowsThirdBeneficiary()
    {
        // Chuẩn bị: XS (2 suất) + 1 suất mua thêm; A và B đã ở gói.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 1);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid c = await CreateBeneficiaryIdAsync(client, vaultId, CitizenC, "Người C");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, b)).StatusCode);

        // Hành động
        HttpResponseMessage addC = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, c);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, addC.StatusCode);
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal(3, vault.GetProperty("usedBeneficiarySlots").GetInt32());
        Assert.Equal(1, vault.GetProperty("extraBeneficiarySlots").GetInt32());
        Assert.Equal(3, vault.GetProperty("beneficiarySlots").GetInt32());
    }

    [SqlFact]
    public async Task Setup16_RemovingLastBeneficiaryOfLastReadyContainerNeedsConfirmation()
    {
        // Chuẩn bị: gói READY (1 tài sản + 1 người nhận), kế hoạch ACTIVE.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid beneficiaryId = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói sẵn sàng");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId)).StatusCode);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        string url = ContainerBeneficiaryUrl(vaultId, containerId, beneficiaryId);

        // Hành động: gỡ khi chưa xác nhận.
        HttpResponseMessage unconfirmed = await client.DeleteAsync(url);

        // Kiểm tra: bị chặn, người nhận vẫn trong gói, kế hoạch vẫn ACTIVE.
        await ApiTestHelpers.AssertProblemAsync(unconfirmed, HttpStatusCode.Conflict, "PLAN_DEACTIVATION_CONFIRMATION_REQUIRED");
        JsonElement stillThere = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(ContainerUrl(vaultId, containerId)));
        Assert.Equal("READY", stillThere.GetProperty("status").GetString());
        Assert.Equal(new List<Guid> { beneficiaryId }, ReadGuids(stillThere.GetProperty("beneficiaryIds")));
        JsonElement vaultBefore = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("ACTIVE", vaultBefore.GetProperty("planStatus").GetString());

        // Hành động: gỡ có xác nhận.
        HttpResponseMessage confirmed = await client.DeleteAsync($"{url}?confirmPlanDeactivation=true");

        // Kiểm tra: gói về DRAFT, kế hoạch về DRAFT.
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        JsonElement container = await ApiTestHelpers.ReadJsonAsync(confirmed);
        Assert.Equal("DRAFT", container.GetProperty("status").GetString());
        Assert.Equal(0, container.GetProperty("beneficiaryIds").GetArrayLength());
        JsonElement vaultAfter = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("DRAFT", vaultAfter.GetProperty("planStatus").GetString());
    }

    [SqlFact]
    public async Task Setup16_ArchivingContainerRemovesBeneficiariesAndFreesSlot()
    {
        // Chuẩn bị: A chỉ ở gói sẽ lưu trữ, B ở gói khác.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid archivedId = await CreateContainerIdAsync(client, vaultId, "Gói lưu trữ");
        Guid keptId = await CreateContainerIdAsync(client, vaultId, "Gói giữ");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, archivedId, a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, keptId, b)).StatusCode);

        // Hành động
        HttpResponseMessage archive = await client.DeleteAsync(ContainerUrl(vaultId, archivedId));

        // Kiểm tra: gói không còn người nhận, hồ sơ A vẫn còn, A hết chiếm suất.
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        JsonElement archived = await ApiTestHelpers.ReadJsonAsync(archive);
        Assert.Equal("ARCHIVED", archived.GetProperty("status").GetString());
        Assert.Equal(0, archived.GetProperty("beneficiaryIds").GetArrayLength());
        Assert.Equal(0, await CountDesignationsAsync(archivedId));
        Assert.Equal(1, await GetUsedSlotsAsync(client));
        JsonElement profileA = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(BeneficiaryUrl(vaultId, a)));
        Assert.Equal(0, profileA.GetProperty("containerIds").GetArrayLength());
    }

    [SqlFact]
    public async Task Oplan01_GraceReadOnlyBlocksAddButAllowsRemove()
    {
        // Chuẩn bị: A đã ở gói, rồi chuyển dịch vụ sang ân hạn chỉ đọc.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, a)).StatusCode);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage add = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, b);
        HttpResponseMessage remove = await client.DeleteAsync(ContainerBeneficiaryUrl(vaultId, containerId, a));

        // Kiểm tra: gắn là thao tác "thêm" → chặn; gỡ là thao tác "giảm" → cho.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(add, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("SERVICE_READ_ONLY", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(0, (await ApiTestHelpers.ReadJsonAsync(remove)).GetProperty("beneficiaryIds").GetArrayLength());
    }

    [SqlFact]
    public async Task MinorBeneficiaryMakesContainerReadyAndUsesSlot()
    {
        // Chuẩn bị: người nhận 10 tuổi (không cần xác nhận); gói có tài sản.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string tenYearsAgo = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime)
            .AddYears(-10).ToString("yyyy-MM-dd");
        HttpResponseMessage createMinor = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = CitizenA, fullName = "Bé A", dateOfBirth = tenYearsAgo, email = ApiTestHelpers.BeneficiaryEmail });
        Assert.Equal(HttpStatusCode.Created, createMinor.StatusCode);
        Guid minorId = (await ApiTestHelpers.ReadJsonAsync(createMinor)).GetProperty("id").GetGuid();
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId)).StatusCode);

        // Hành động
        HttpResponseMessage add = await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, minorId);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal("READY", (await ApiTestHelpers.ReadJsonAsync(add)).GetProperty("status").GetString());
        Assert.Equal(1, await GetUsedSlotsAsync(client));
    }

    [SqlFact]
    public async Task BeneficiaryInUse_DeleteBlockedUntilRemovedFromContainers()
    {
        // Chuẩn bị: A ở 2 gói.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid first = await CreateContainerIdAsync(client, vaultId, "Gói 1");
        Guid second = await CreateContainerIdAsync(client, vaultId, "Gói 2");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, first, a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, second, a)).StatusCode);

        // Hành động
        HttpResponseMessage blocked = await client.DeleteAsync(BeneficiaryUrl(vaultId, a));

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "BENEFICIARY_IN_USE");
        Assert.Equal(new List<Guid> { first, second }, ReadGuids(problem.GetProperty("containerIds")));

        // Hành động: gỡ khỏi cả hai gói rồi xóa.
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(ContainerBeneficiaryUrl(vaultId, first, a))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(ContainerBeneficiaryUrl(vaultId, second, a))).StatusCode);
        HttpResponseMessage deleted = await client.DeleteAsync(BeneficiaryUrl(vaultId, a));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [SqlFact]
    public async Task Setup04_EditingDesignatedBeneficiaryBumpsConfigVersion()
    {
        // Chuẩn bị: A ở gói, B chưa ở gói nào.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid containerId = await CreateContainerIdAsync(client, vaultId, "Gói");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, a)).StatusCode);
        int versionBefore = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động: sửa A.
        HttpResponseMessage editA = await client.PutAsJsonAsync(BeneficiaryUrl(vaultId, a),
            new { etag = "\"0\"", fullName = "Người A đổi tên" });
        int versionAfterA = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động: sửa B.
        HttpResponseMessage editB = await client.PutAsJsonAsync(BeneficiaryUrl(vaultId, b),
            new { etag = "\"0\"", fullName = "Người B đổi tên" });
        int versionAfterB = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Kiểm tra: hồ sơ trong gói tăng 1; hồ sơ ngoài gói không tăng.
        Assert.Equal(HttpStatusCode.OK, editA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, editB.StatusCode);
        Assert.Equal(versionBefore + 1, versionAfterA);
        Assert.Equal(versionAfterA, versionAfterB);
    }

    [SqlFact]
    public async Task Quota04_GetBeneficiaryReturnsDesignatedContainerIds()
    {
        // Chuẩn bị: A ở gói 1 và gói 3, không ở gói 2.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid first = await CreateContainerIdAsync(client, vaultId, "Gói 1");
        await CreateContainerIdAsync(client, vaultId, "Gói 2");
        Guid third = await CreateContainerIdAsync(client, vaultId, "Gói 3");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, first, a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, third, a)).StatusCode);

        // Hành động
        JsonElement profile = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(BeneficiaryUrl(vaultId, a)));
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(BeneficiariesUrl(vaultId)));

        // Kiểm tra: GET một hồ sơ và GET danh sách cùng trả đúng các gói theo thứ tự gắn.
        Assert.Equal(new List<Guid> { first, third }, ReadGuids(profile.GetProperty("containerIds")));
        JsonElement item = page.GetProperty("items")[0];
        Assert.Equal(new List<Guid> { first, third }, ReadGuids(item.GetProperty("containerIds")));
    }

    private static string BeneficiariesUrl(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/beneficiaries";
    }

    private static string BeneficiaryUrl(Guid vaultId, Guid beneficiaryId)
    {
        return $"{BeneficiariesUrl(vaultId)}/{beneficiaryId}";
    }

    private static string ContainerUrl(Guid vaultId, Guid containerId)
    {
        return $"/api/v1/vaults/{vaultId}/containers/{containerId}";
    }

    private static string ContainerBeneficiaryUrl(Guid vaultId, Guid containerId, Guid beneficiaryId)
    {
        return $"{ContainerUrl(vaultId, containerId)}/beneficiaries/{beneficiaryId}";
    }

    private static async Task<Guid> CreateBeneficiaryIdAsync(HttpClient client, Guid vaultId, string citizenId, string fullName)
    {
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, citizenId, fullName);
        return beneficiary.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateContainerIdAsync(HttpClient client, Guid vaultId, string name)
    {
        JsonElement container = await ApiTestHelpers.CreateContainerAsync(client, vaultId, name);
        return container.GetProperty("id").GetGuid();
    }

    private static async Task<int> GetUsedSlotsAsync(HttpClient client)
    {
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        return vault.GetProperty("usedBeneficiarySlots").GetInt32();
    }

    /// <summary>Số dòng Designations của một gói (đọc thẳng DB).</summary>
    private async Task<int> CountDesignationsAsync(Guid containerId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Designations.CountAsync(d => d.ContainerId == containerId);
        });
        return count;
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
