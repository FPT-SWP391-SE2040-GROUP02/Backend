using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Chủ kho gán tài sản trực tiếp cho người nhận, đếm suất, nhận chung qua HTTP trên SQL Server thật
/// (docs/api-contract.md mục 6.6).
/// </summary>
/// <remarks>
/// [SqlFact] chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION; mỗi lớp test dùng một database tạm.
/// Kho XS (CreateClientWithPaidVaultAsync) có 2 suất người nhận. Dữ liệu khó tạo qua API (hạng, suất mua thêm,
/// kế hoạch đang chạy, ân hạn) thì sửa thẳng DB ở phần "Chuẩn bị".
/// </remarks>
public sealed class AssetAssignmentFlowTests : IAsyncLifetime
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
    public async Task Assign_OneBeneficiary_ReturnsRowAndShowsInListAndVault()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Sổ tiết kiệm");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        int versionBefore = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động
        JsonElement rows = await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);

        // Kiểm tra: một dòng gán, có trong danh sách của kho, kho đếm 1 tài sản đã gán, phiên bản cấu hình tăng
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(assetId, rows[0].GetProperty("assetId").GetGuid());
        Assert.Equal(a, rows[0].GetProperty("beneficiaryId").GetGuid());
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(AssignmentsUrl(vaultId)));
        Assert.Equal(1, list.GetArrayLength());
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal(1, vault.GetProperty("assignedAssetCount").GetInt32());
        Assert.Equal(versionBefore + 1, await ApiTestHelpers.GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Assign_SharedAsset_CreatesOneRowPerBeneficiary()
    {
        // Chuẩn bị: hai người cùng nhận một tài sản (nhận chung)
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Nhà");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");

        // Hành động
        JsonElement rows = await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a, b);

        // Kiểm tra: hai dòng cùng tài sản, tài sản đếm một lần
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(new List<Guid> { a, b }, ReadGuids(rows, "beneficiaryId"));
        Assert.Equal(2, await CountAssignmentsAsync(vaultId));
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal(1, vault.GetProperty("assignedAssetCount").GetInt32());
    }

    [SqlFact]
    public async Task Assign_SameSetTwice_IsIdempotentAndKeepsConfigVersion()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        int versionAfterFirst = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động: gửi lại đúng danh sách cũ (bấm lặp)
        JsonElement again = await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);

        // Kiểm tra: vẫn một dòng, không ghi thêm, phiên bản cấu hình không tăng
        Assert.Equal(1, again.GetArrayLength());
        Assert.Equal(1, await CountAssignmentsAsync(vaultId));
        Assert.Equal(versionAfterFirst, await ApiTestHelpers.GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Assign_DuplicateIdsInRequest_AreCountedOnce()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");

        // Hành động
        JsonElement rows = await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a, a);

        // Kiểm tra
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(1, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task SetRecipients_ReplacesTheWholeSet()
    {
        // Chuẩn bị: tài sản đang gán cho A và B; hồ sơ C đã có nhưng chưa được gán.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid c = await CreateBeneficiaryIdAsync(client, vaultId, CitizenC, "Người C");
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a, b);

        // Hành động: đổi danh sách thành B và C (A rời đi, C vào)
        JsonElement rows = await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, b, c);

        // Kiểm tra: danh sách mới là B, C; A không còn được gán
        List<Guid> recipients = ReadGuids(rows, "beneficiaryId");
        Assert.Equal(2, recipients.Count);
        Assert.Contains(b, recipients);
        Assert.Contains(c, recipients);
        Assert.Equal(2, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task SetRecipients_EmptyList_UnassignsEveryone()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a, b);

        // Hành động
        JsonElement rows = await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId);

        // Kiểm tra
        Assert.Equal(0, rows.GetArrayLength());
        Assert.Equal(0, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task Assign_FreeVault_IsForbidden()
    {
        // Chuẩn bị: tạo hồ sơ khi kho còn trả phí, rồi đưa kho về Free
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Tier, ServiceTier.Free)
                    .SetProperty(e => e.Status, EntitlementStatus.Free));
        });

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, a);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "PAID_TIER_REQUIRED");
        Assert.Equal(0, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task Assign_BeneficiaryOfAnotherVault_IsNotFound()
    {
        // Chuẩn bị: hồ sơ thuộc kho khác
        (HttpClient other, Guid otherVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid otherBeneficiary = await CreateBeneficiaryIdAsync(other, otherVaultId, CitizenA, "Người A");
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, otherBeneficiary);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "BENEFICIARY_NOT_FOUND");
        Assert.Equal(0, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task Assign_AssetOfAnotherVault_IsNotFound()
    {
        // Chuẩn bị: tài sản thuộc kho khác
        (HttpClient other, Guid otherVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid otherAsset = await ApiTestHelpers.CreateTextAssetAsync(other, otherVaultId, "Của người khác");
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.AssignAssetAsync(client, vaultId, otherAsset, a);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "ASSET_NOT_FOUND");
    }

    [SqlFact]
    public async Task Assign_StrangerCannotReadOrChangeAnotherOwnersAssignments()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(owner, vaultId, CitizenA, "Người A");
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động và kiểm tra: kho người khác trả 404 như không tồn tại
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(AssignmentsUrl(vaultId))).StatusCode);
        HttpResponseMessage put = await ApiTestHelpers.AssignAssetAsync(stranger, vaultId, assetId, a);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        HttpResponseMessage delete = await ApiTestHelpers.UnassignAssetAsync(stranger, vaultId, assetId, a);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [SqlFact]
    public async Task Assign_ThreeDistinctBeneficiaries_HasNoRecipientLimit()
    {
        // Chuẩn bị: gói trả phí không giới hạn số người nhận; A và B đã được gán
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid c = await CreateBeneficiaryIdAsync(client, vaultId, CitizenC, "Người C");
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a, b);

        // Hành động
        HttpResponseMessage addC = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, a, b, c);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, addC.StatusCode);
        Assert.Equal(3, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task Unassign_RemovesOnlyTheGivenPair()
    {
        // Chuẩn bị: A nhận 2 tài sản, B nhận 1 tài sản → 3 dòng gán
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid first = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú 1");
        Guid second = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú 2");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, first, a, b);
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, second, a);

        // Hành động: gỡ A khỏi tài sản thứ nhất
        HttpResponseMessage removeOne = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, first, a);

        // Kiểm tra: chỉ mất đúng một dòng gán
        Assert.Equal(HttpStatusCode.OK, removeOne.StatusCode);
        Assert.Equal(2, await CountAssignmentsAsync(vaultId));

        // Hành động: gỡ A khỏi tài sản còn lại
        HttpResponseMessage removeAll = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, second, a);

        // Kiểm tra: chỉ còn dòng gán của B
        Assert.Equal(HttpStatusCode.OK, removeAll.StatusCode);
        Assert.Equal(1, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task Unassign_PairNotAssigned_ReturnsCurrentStateWithoutWriting()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        int versionBefore = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động: gỡ người chưa được gán
        HttpResponseMessage response = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, b);

        // Kiểm tra: trả dòng gán còn lại, không ghi gì
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement rows = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(versionBefore, await ApiTestHelpers.GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Unassign_LastAssignmentOfRunningPlan_NeedsConfirmation()
    {
        // Chuẩn bị: một tài sản gán cho một người, kế hoạch đang chạy
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        // Hành động: gỡ khi chưa xác nhận
        HttpResponseMessage unconfirmed = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a);

        // Kiểm tra: bị chặn, dòng gán còn, kế hoạch vẫn ACTIVE
        await ApiTestHelpers.AssertProblemAsync(unconfirmed, HttpStatusCode.Conflict, "PLAN_DEACTIVATION_CONFIRMATION_REQUIRED");
        Assert.Equal(1, await CountAssignmentsAsync(vaultId));
        JsonElement vaultBefore = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("ACTIVE", vaultBefore.GetProperty("planStatus").GetString());

        // Hành động: gỡ có xác nhận
        HttpResponseMessage confirmed = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a, confirmPlanDeactivation: true);

        // Kiểm tra: không còn dòng gán, kế hoạch về DRAFT
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(0, await CountAssignmentsAsync(vaultId));
        JsonElement vaultAfter = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("DRAFT", vaultAfter.GetProperty("planStatus").GetString());
    }

    [SqlFact]
    public async Task Unassign_WhileOtherAssignmentsRemain_NeedsNoConfirmation()
    {
        // Chuẩn bị: hai tài sản được gán, kế hoạch đang chạy
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid first = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú 1");
        Guid second = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú 2");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, first, a);
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, second, a);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        // Hành động
        HttpResponseMessage response = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, first, a);

        // Kiểm tra: gỡ được ngay, kế hoạch vẫn ACTIVE
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("ACTIVE", vault.GetProperty("planStatus").GetString());
    }

    [SqlFact]
    public async Task Oplan01_GraceReadOnlyBlocksAddButAllowsRemove()
    {
        // Chuẩn bị: A đã được gán, rồi dịch vụ chuyển sang ân hạn chỉ đọc
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid b = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage add = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, a, b);
        HttpResponseMessage remove = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a);

        // Kiểm tra: thêm bị chặn, gỡ vẫn được
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(add, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("SERVICE_READ_ONLY", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(0, await CountAssignmentsAsync(vaultId));
    }

    [SqlFact]
    public async Task DeleteAsset_IsBlockedWhileAssigned()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);

        // Hành động và kiểm tra: đang được gán thì không xóa được, gỡ gán xong thì xóa được
        HttpResponseMessage blocked = await client.DeleteAsync($"/api/v1/vaults/{vaultId}/assets/{assetId}");
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "ASSET_IN_USE");
        Assert.Equal("ASSIGNED", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a);
        HttpResponseMessage deleted = await client.DeleteAsync($"/api/v1/vaults/{vaultId}/assets/{assetId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [SqlFact]
    public async Task DeleteBeneficiary_IsBlockedWhileAssignedAndListsAssetIds()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        string url = $"/api/v1/vaults/{vaultId}/beneficiaries/{a}";

        // Hành động
        JsonElement profile = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(url));
        HttpResponseMessage blocked = await client.DeleteAsync(url);

        // Kiểm tra: hồ sơ báo tài sản đang được gán; xóa bị chặn kèm assetIds; gỡ gán xong thì xóa được
        Assert.Equal(new List<Guid> { assetId }, ReadGuids(profile.GetProperty("assetIds")));
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "BENEFICIARY_IN_USE");
        Assert.Equal(new List<Guid> { assetId }, ReadGuids(problem.GetProperty("assetIds")));
        await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
    }

    [SqlFact]
    public async Task Assign_TwoParallelRequestsForSamePair_NeverDuplicateOrFailWith500()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");

        // Hành động: hai yêu cầu cùng gán một cặp
        HttpResponseMessage[] responses = await Task.WhenAll(
            ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, a),
            ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, a));

        // Kiểm tra: ít nhất một bên thành công, bên kia thành công hoặc báo xung đột; không 500; đúng một dòng
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(1, await CountAssignmentsAsync(vaultId));
    }

    private static string AssignmentsUrl(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/assignments";
    }

    private static async Task<Guid> CreateBeneficiaryIdAsync(HttpClient client, Guid vaultId, string citizenId, string fullName)
    {
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, citizenId, fullName);
        return beneficiary.GetProperty("id").GetGuid();
    }


    /// <summary>Số dòng AssetAssignments của kho (đọc thẳng DB).</summary>
    private async Task<int> CountAssignmentsAsync(Guid vaultId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.AssetAssignments.CountAsync(a => a.Asset.VaultId == vaultId);
        });
        return count;
    }

    private static List<Guid> ReadGuids(JsonElement array, string? property = null)
    {
        List<Guid> values = new List<Guid>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (property == null)
            {
                values.Add(item.GetGuid());
            }
            else
            {
                values.Add(item.GetProperty(property).GetGuid());
            }
        }
        return values;
    }
}
