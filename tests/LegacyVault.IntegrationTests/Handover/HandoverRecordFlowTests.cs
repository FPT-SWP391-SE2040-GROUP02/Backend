using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Biên bản bàn giao qua HTTP trên SQL Server thật: Người thực thi mở bàn giao, luật sư lập, chốt, mở lại,
/// kiểm thứ tự ba bước (biên bản → danh tính → chữ ký ba bên) và quyền của từng vai.
/// Môi trường test bỏ qua xác minh danh tính nên nhận lượt xong là coi như luật sư đã xác nhận.
/// </summary>
public sealed class HandoverRecordFlowTests : IAsyncLifetime
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

    private sealed class Vault2
    {
        public HttpClient Owner { get; set; } = null!;
        public Guid VaultId { get; set; }
        public Guid AssetA { get; set; }
        public Guid AssetB { get; set; }
        public Guid FirstRecipient { get; set; }
        public Guid SecondRecipient { get; set; }
    }

    private static string RecipientEmail(int index)
    {
        return "bb-nhan" + index + "@example.com";
    }

    /// <summary>Kho đã kích hoạt: hai tài sản chữ trong một gói, gói chỉ định cho người nhận đầu; người nhận thứ hai chưa có gói.</summary>
    private async Task<Vault2> BuildVaultAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetA = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        Guid assetB = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Xe");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói M")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetB)).StatusCode);
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000011", ApiTestHelpers.BeneficiaryName(0), RecipientEmail(0));
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000012", ApiTestHelpers.BeneficiaryName(1), RecipientEmail(1));
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            owner, vaultId, containerId, first.GetProperty("id").GetGuid())).StatusCode);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        Vault2 result = new Vault2();
        result.Owner = owner;
        result.VaultId = vaultId;
        result.AssetA = assetA;
        result.AssetB = assetB;
        result.FirstRecipient = first.GetProperty("id").GetGuid();
        result.SecondRecipient = second.GetProperty("id").GetGuid();
        return result;
    }

    private static Task<HttpResponseMessage> SetLinesAsync(HttpClient verifier, Guid recordId, params (Guid BeneficiaryId, Guid AssetId)[] lines)
    {
        List<object> body = new List<object>();
        foreach ((Guid beneficiaryId, Guid assetId) in lines)
        {
            body.Add(new { beneficiaryId, assetId });
        }
        return verifier.PutAsJsonAsync(HandoverRecordTestData.VerifierRecordUrl(recordId) + "/lines", new { lines = body });
    }

    private static async Task<JsonElement> PostOkAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.PostAsync(url, null);
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, url + " -> " + response.StatusCode + ": " + text);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    [SqlFact]
    public async Task Open_CreatesOneRecordLocksVaultAndAssignsAVerifier()
    {
        Vault2 vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);

        // Chưa mở bàn giao: chưa có biên bản.
        HttpResponseMessage none = await executor.Client.GetAsync(HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId));
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);

        JsonElement opened = await HandoverRecordTestData.OpenAsync(executor.Client, vault.VaultId);
        JsonElement again = await HandoverRecordTestData.OpenAsync(executor.Client, vault.VaultId);

        Assert.Equal("DRAFTING", opened.GetProperty("status").GetString());
        Assert.Equal(opened.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverRecord record = await db.Set<HandoverRecord>().SingleAsync(r => r.VaultId == vault.VaultId);
            Assert.Equal(UserRole.Verifier, (await db.Users.SingleAsync(u => u.Id == record.VerifierUserId)).Role);
            Assert.Equal(executor.User.Id, record.RequestedByUserId);
            Assert.Equal(PlanStatus.HandoverRequested, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vault.VaultId)).Status);
        });

        // Kho đã khóa: Chủ kho không thêm tài sản được nữa.
        HttpResponseMessage locked = await vault.Owner.PostAsJsonAsync(
            $"/api/v1/vaults/{vault.VaultId}/assets/text", new { name = "Mới", content = "x" });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
    }

    [SqlFact]
    public async Task Open_ByOwnerOrStranger_IsNotFound_AndWithoutVerifier_IsConflict()
    {
        Vault2 vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        HttpResponseMessage byOwner = await vault.Owner.PostAsync(HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId), null);
        Assert.Equal(HttpStatusCode.NotFound, byOwner.StatusCode);

        // Không còn luật sư nào đang hoạt động (database tạm dùng chung trong lớp nên tắt hết luật sư của test khác).
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Role == UserRole.Verifier)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, UserStatus.Disabled));
        });
        HttpResponseMessage noVerifier = await executor.Client.PostAsync(HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId), null);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(noVerifier, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("NO_VERIFIER", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Executor_CannotSeeAssetsBeforeTheRecordIsFinalized()
    {
        Vault2 vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        string assetsUrl = $"/api/v1/executor/vaults/{vault.VaultId}/assets";

        HttpResponseMessage beforeOpen = await executor.Client.GetAsync(assetsUrl);
        Assert.Equal(HttpStatusCode.Conflict, beforeOpen.StatusCode);
        JsonElement opened = await HandoverRecordTestData.OpenAsync(executor.Client, vault.VaultId);
        HttpResponseMessage drafting = await executor.Client.GetAsync(assetsUrl);
        Assert.Equal(HttpStatusCode.Conflict, drafting.StatusCode);

        Guid openedId = opened.GetProperty("id").GetGuid();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, openedId);
        await HandoverRecordTestData.UseOwnerSplitAndFinalizeAsync(verifier.Client, openedId);

        HttpResponseMessage after = await executor.Client.GetAsync(assetsUrl);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [SqlFact]
    public async Task Verifier_StepsAreLockedInOrder()
    {
        Vault2 vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = (await HandoverRecordTestData.OpenAsync(executor.Client, vault.VaultId)).GetProperty("id").GetGuid();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);

        // Bước 1 chưa có dòng: chưa chốt được; chưa tới bước ký thì không ai ký được.
        await ApiTestHelpers.AssertProblemAsync(await verifier.Client.PostAsync(url + "/finalize", null), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        JsonElement early = await ApiTestHelpers.AssertProblemAsync(
            await verifier.Client.PostAsync(url + "/sign", null), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("NOT_SIGNING_STEP", early.GetProperty("reason").GetString());

        // Dòng sai: tài sản lạ, trùng cặp người - tài sản.
        await ApiTestHelpers.AssertProblemAsync(
            await SetLinesAsync(verifier.Client, recordId, (vault.FirstRecipient, Guid.NewGuid())), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await SetLinesAsync(verifier.Client, recordId, (vault.FirstRecipient, vault.AssetA), (vault.FirstRecipient, vault.AssetA)),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        // Luật sư khác chủ kho chọn: một tài sản có thể giao cho hai người (đồng thừa kế).
        HttpResponseMessage set = await SetLinesAsync(verifier.Client, recordId,
            (vault.FirstRecipient, vault.AssetA), (vault.FirstRecipient, vault.AssetB), (vault.SecondRecipient, vault.AssetB));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        JsonElement finalized = await PostOkAsync(verifier.Client, url + "/finalize");

        Assert.Equal("IDENTITY_CHECK", finalized.GetProperty("status").GetString());
        Assert.Equal(2, finalized.GetProperty("recipients").GetArrayLength());
        // Đã chốt thì không sửa dòng được nữa.
        await ApiTestHelpers.AssertProblemAsync(
            await SetLinesAsync(verifier.Client, recordId, (vault.FirstRecipient, vault.AssetA)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverTurn> turns = await db.Set<HandoverTurn>().Include(t => t.Items).Where(t => t.RecordId == recordId).ToListAsync();
            Assert.Equal(2, turns.Count);
            Assert.Equal(2, turns.Single(t => t.BeneficiaryId == vault.FirstRecipient).Items.Count);
            Assert.Single(turns.Single(t => t.BeneficiaryId == vault.SecondRecipient).Items);
            Assert.Equal(PlanStatus.HandoverInProgress, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vault.VaultId)).Status);
        });
    }

    [SqlFact]
    public async Task OtherVerifier_GetsNotFound()
    {
        Vault2 vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = (await HandoverRecordTestData.OpenAsync(executor.Client, vault.VaultId)).GetProperty("id").GetGuid();
        VaultTestData.ExecutorClient other = await HandoverRecordTestData.CreateVerifierClientAsync(factory);

        HttpResponseMessage response = await other.Client.GetAsync(HandoverRecordTestData.VerifierRecordUrl(recordId));

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(await other.Client.GetAsync("/api/v1/verifier/handover-records"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [SqlFact]
    public async Task FullFlow_ThreePartySignatures_OpenDownloads_AndReopenMakesEveryoneSignAgain()
    {
        Vault2 vault = await BuildVaultAsync();
        HandoverRecordTestData.FinalizedRecord record = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vault.VaultId);
        Assert.Equal("IDENTITY_CHECK", record.Body.GetProperty("status").GetString());
        string verifierUrl = HandoverRecordTestData.VerifierRecordUrl(record.RecordId);

        // Người nhận mở link và nhận lượt; danh tính đủ thì biên bản sang bước ký.
        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail(0));
        JsonElement invitations = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync("/api/v1/handover/invitations/mine"));
        Guid turnId = invitations[0].GetProperty("turnId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null)).StatusCode);
        JsonElement view = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync($"/api/v1/handover/turns/{turnId}/record"));
        Assert.Equal("SIGNING", view.GetProperty("status").GetString());
        Assert.True(view.GetProperty("canSign").GetBoolean());
        Assert.Equal(1, view.GetProperty("recipients").GetArrayLength());

        // Luật sư ký rồi mở lại: chữ ký cũ mất hiệu lực, bản mới.
        await PostOkAsync(record.Verifier.Client, verifierUrl + "/sign");
        JsonElement reopened = await PostOkAsync(record.Verifier.Client, verifierUrl + "/reopen");
        Assert.Equal("DRAFTING", reopened.GetProperty("status").GetString());
        Assert.Equal(2, reopened.GetProperty("revisionNo").GetInt32());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("verifierSignedAt").ValueKind);
        // Chốt lại: người nhận đã được xác nhận danh tính thì sang bước ký ngay.
        JsonElement refinalized = await PostOkAsync(record.Verifier.Client, verifierUrl + "/finalize");
        Assert.Equal("SIGNING", refinalized.GetProperty("status").GetString());

        await HandoverRecordTestData.SignAsVerifierAndExecutorAsync(record, vault.VaultId);
        // Người thực thi chưa gửi được mã khi biên bản chưa đủ chữ ký.
        HttpResponseMessage tooEarly = await record.Executor.Client.PostAsJsonAsync(
            $"/api/v1/executor/handover-records/{record.RecordId}/handover-turns/{turnId}/send-key", new { key = "x" });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        JsonElement completed = await HandoverRecordTestData.SignAsRecipientAsync(recipient, turnId);
        Assert.Equal("COMPLETED", completed.GetProperty("status").GetString());
        JsonElement turn = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync($"/api/v1/handover/turns/{turnId}"));
        Assert.Equal("HANDOVER_COMMITTED", turn.GetProperty("status").GetString());
        Assert.Equal(2, turn.GetProperty("items").GetArrayLength());

        // Đã đủ chữ ký: không ký thêm, không mở lại được.
        await ApiTestHelpers.AssertProblemAsync(
            await record.Verifier.Client.PostAsync(verifierUrl + "/reopen", null), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<HandoverGrant>().CountAsync(g => g.TurnId == turnId));
            Assert.Equal(3, await db.Set<HandoverRecordSignature>().CountAsync(x => x.RecordId == record.RecordId && x.RevisionNo == 2));
        });

        // Người thực thi xem tiến độ theo biên bản.
        JsonElement progress = await ApiTestHelpers.ReadJsonAsync(await record.Executor.Client.GetAsync(
            $"/api/v1/executor/handover-records/{record.RecordId}/handover-progress"));
        Assert.True(progress.GetProperty("handoverStarted").GetBoolean());
        Assert.Equal(1, progress.GetProperty("turns").GetArrayLength());

        // Danh sách kho của Người thực thi kèm tình trạng biên bản.
        JsonElement vaults = await ApiTestHelpers.ReadJsonAsync(await record.Executor.Client.GetAsync("/api/v1/executor/vaults"));
        Assert.Equal("COMPLETED", vaults[0].GetProperty("handoverRecordStatus").GetString());
    }

    [SqlFact]
    public async Task Reopen_RemovingARecipientCancelsTheirTurn_AddingOneCreatesANewTurn()
    {
        Vault2 vault = await BuildVaultAsync();
        HandoverRecordTestData.FinalizedRecord record = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vault.VaultId);
        string url = HandoverRecordTestData.VerifierRecordUrl(record.RecordId);

        await PostOkAsync(record.Verifier.Client, url + "/reopen");
        Assert.Equal(HttpStatusCode.OK, (await SetLinesAsync(record.Verifier.Client, record.RecordId,
            (vault.SecondRecipient, vault.AssetA), (vault.SecondRecipient, vault.AssetB))).StatusCode);
        await PostOkAsync(record.Verifier.Client, url + "/finalize");

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverTurn> turns = await db.Set<HandoverTurn>().Where(t => t.RecordId == record.RecordId).ToListAsync();
            Assert.Equal(HandoverTurnStatus.Cancelled, turns.Single(t => t.BeneficiaryId == vault.FirstRecipient).Status);
            Assert.Equal(HandoverTurnStatus.Invited, turns.Single(t => t.BeneficiaryId == vault.SecondRecipient).Status);
        });
    }
}
