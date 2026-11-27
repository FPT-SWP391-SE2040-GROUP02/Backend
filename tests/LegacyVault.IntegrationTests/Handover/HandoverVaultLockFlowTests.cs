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
/// Thời điểm khóa kho khi có yêu cầu nhận tài sản (SQL Server thật): gửi yêu cầu chỉ tạo biên bản, kho chỉ khóa khi luật sư
/// chấp nhận giấy tờ; yêu cầu bổ sung hay từ chối không khóa; từ chối hết thì biên bản tự đóng.
/// </summary>
public sealed class HandoverVaultLockFlowTests : IAsyncLifetime
{
    private const string RequestsUrl = "/api/v1/handover/requests";

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

    private sealed class LockVault
    {
        public HttpClient Owner { get; set; } = null!;
        public Guid VaultId { get; set; }
        public Guid AssetA { get; set; }
        public Guid AssetB { get; set; }
        public Guid FirstRecipient { get; set; }
        public Guid SecondRecipient { get; set; }
        public HttpClient FirstClient { get; set; } = null!;
        public HttpClient SecondClient { get; set; } = null!;
    }

    private static string RecipientEmail(Guid vaultSeed, int index)
    {
        return "khoa-" + vaultSeed.ToString("N").Substring(0, 8) + "-" + index + "@example.com";
    }

    /// <summary>
    /// Kho đã kích hoạt: hai tài sản gán cho người nhận đầu, một tài sản gán cho người nhận thứ hai; cả hai người nhận đã có
    /// tài khoản xác minh email. Có Người thực thi và một luật sư rảnh.
    /// </summary>
    private async Task<LockVault> BuildVaultAsync()
    {
        Guid seed = Guid.NewGuid();
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetA = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        Guid assetB = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Xe");
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000021", ApiTestHelpers.BeneficiaryName(0), RecipientEmail(seed, 0));
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000022", ApiTestHelpers.BeneficiaryName(1), RecipientEmail(seed, 1));
        Guid firstId = first.GetProperty("id").GetGuid();
        Guid secondId = second.GetProperty("id").GetGuid();
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetA, firstId);
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetB, firstId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);

        LockVault result = new LockVault();
        result.Owner = owner;
        result.VaultId = vaultId;
        result.AssetA = assetA;
        result.AssetB = assetB;
        result.FirstRecipient = firstId;
        result.SecondRecipient = secondId;
        result.FirstClient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail(seed, 0));
        result.SecondClient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail(seed, 1));
        return result;
    }

    /// <summary>Người nhận gửi yêu cầu trong app; phải trả 200. Trả (id yêu cầu, id biên bản).</summary>
    private static async Task<(Guid ClaimId, Guid RecordId)> SubmitOkAsync(HttpClient recipient, Guid vaultId)
    {
        HttpResponseMessage response = await recipient.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vaultId, "Bố tôi mất"));
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, "by-account -> " + response.StatusCode + ": " + text);
        JsonElement claim = await ApiTestHelpers.ReadJsonAsync(response);
        return (claim.GetProperty("id").GetGuid(), claim.GetProperty("recordId").GetGuid());
    }

    private static Task<HttpResponseMessage> ReviewAsync(
        HttpClient verifier, Guid recordId, Guid claimId, string decision, string? reason)
    {
        return verifier.PostAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/claims/" + claimId + "/review", new { decision, reason });
    }

    private async Task<PlanStatus> ReadPlanStatusAsync(Guid vaultId)
    {
        PlanStatus status = PlanStatus.Draft;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            status = (await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId)).Status;
        });
        return status;
    }

    private static Task<HttpResponseMessage> AddAssetAsync(LockVault vault)
    {
        return vault.Owner.PostAsJsonAsync($"/api/v1/vaults/{vault.VaultId}/assets/text", new { name = "Mới", content = "x" });
    }

    [SqlFact]
    public async Task Submit_DoesNotLock_SupplementAndRejectKeepItOpen_AcceptLocks()
    {
        // Chuẩn bị
        LockVault vault = await BuildVaultAsync();
        string ownerUrl = $"/api/v1/vaults/{vault.VaultId}/handover-request";

        // Hành động: người nhận đầu gửi yêu cầu
        (Guid firstClaim, Guid recordId) = await SubmitOkAsync(vault.FirstClient, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);

        // Kiểm tra: có biên bản nhưng kho chưa khóa, Chủ tài sản thấy yêu cầu và vẫn sửa được kho
        Assert.Equal(PlanStatus.Active, await ReadPlanStatusAsync(vault.VaultId));
        JsonElement ownerView = await ApiTestHelpers.ReadJsonAsync(await vault.Owner.GetAsync(ownerUrl));
        Assert.False(ownerView.GetProperty("vaultLocked").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await AddAssetAsync(vault)).StatusCode);

        // Hành động: luật sư yêu cầu bổ sung; người thứ hai gửi; luật sư từ chối người đầu
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, recordId, firstClaim, "REQUEST_SUPPLEMENT", "Thiếu giấy chứng tử")).StatusCode);
        PlanStatus afterSupplement = await ReadPlanStatusAsync(vault.VaultId);
        (Guid secondClaim, Guid sameRecord) = await SubmitOkAsync(vault.SecondClient, vault.VaultId);
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, recordId, firstClaim, "REJECT", "Không đúng người")).StatusCode);
        PlanStatus afterReject = await ReadPlanStatusAsync(vault.VaultId);
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(
            await verifier.Client.GetAsync(HandoverRecordTestData.VerifierRecordUrl(recordId)));
        HttpResponseMessage deleteRequester = await vault.Owner.DeleteAsync(
            $"/api/v1/vaults/{vault.VaultId}/beneficiaries/{vault.SecondRecipient}");
        List<object> secondLines = new List<object>();
        secondLines.Add(new { beneficiaryId = vault.SecondRecipient, assetId = vault.AssetB });
        HttpResponseMessage setSecond = await verifier.Client.PutAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/lines", new { lines = secondLines });
        HttpResponseMessage finalizePending = await verifier.Client.PostAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/finalize", HandoverRecordTestData.FinalizeBody);

        // Kiểm tra: bổ sung và từ chối không khóa; còn yêu cầu chờ xét nên biên bản vẫn mở và chưa chốt được;
        // người đã gửi yêu cầu không bị Chủ tài sản xóa
        Assert.Equal(PlanStatus.Active, afterSupplement);
        Assert.Equal(PlanStatus.Active, afterReject);
        Assert.Equal(recordId, sameRecord);
        Assert.Equal("DRAFTING", draft.GetProperty("status").GetString());
        Assert.False(draft.GetProperty("vaultLocked").GetBoolean());
        JsonElement inRequest = await ApiTestHelpers.AssertProblemAsync(deleteRequester, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("RECIPIENT_IN_HANDOVER_REQUEST", inRequest.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, setSecond.StatusCode);
        JsonElement pending = await ApiTestHelpers.AssertProblemAsync(finalizePending, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("CLAIMS_PENDING", pending.GetProperty("reason").GetString());

        // Hành động: luật sư chấp nhận người thứ hai
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, recordId, secondClaim, "ACCEPT", null)).StatusCode);

        // Kiểm tra: kho khóa; Chủ tài sản không sửa được nữa; người bị từ chối không ghi tài sản được
        Assert.Equal(PlanStatus.HandoverRequested, await ReadPlanStatusAsync(vault.VaultId));
        JsonElement lockedView = await ApiTestHelpers.ReadJsonAsync(await vault.Owner.GetAsync(ownerUrl));
        Assert.True(lockedView.GetProperty("vaultLocked").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await AddAssetAsync(vault)).StatusCode);
        List<object> lines = new List<object>();
        lines.Add(new { beneficiaryId = vault.FirstRecipient, assetId = vault.AssetA });
        HttpResponseMessage rejectedLine = await verifier.Client.PutAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/lines", new { lines });
        await ApiTestHelpers.AssertProblemAsync(rejectedLine, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task RejectingEveryRequest_ClosesTheUnlockedRecord_AndANewRequestOpensANewRecord()
    {
        // Chuẩn bị
        LockVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitOkAsync(vault.FirstClient, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);

        // Hành động: luật sư từ chối yêu cầu duy nhất
        HttpResponseMessage rejected = await ReviewAsync(verifier.Client, recordId, claimId, "REJECT", "Giấy tờ giả");
        HttpResponseMessage ownerView = await vault.Owner.GetAsync($"/api/v1/vaults/{vault.VaultId}/handover-request");
        JsonElement mine = (await ApiTestHelpers.ReadJsonAsync(await vault.FirstClient.GetAsync(RequestsUrl + "/mine")))[0];
        (Guid _, Guid newRecordId) = await SubmitOkAsync(vault.SecondClient, vault.VaultId);
        (Guid _, Guid againRecordId) = await SubmitOkAsync(vault.FirstClient, vault.VaultId);

        // Kiểm tra: biên bản đóng kèm lý do, kho vẫn mở; lần gửi sau tạo biên bản mới (người bị từ chối gửi lại được)
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ownerView.StatusCode);
        Assert.Equal("REJECTED", mine.GetProperty("status").GetString());
        Assert.Equal("CANCELLED", mine.GetProperty("recordStatus").GetString());
        Assert.Equal("Mọi yêu cầu bị từ chối", mine.GetProperty("cancelReason").GetString());
        Assert.Equal(PlanStatus.Active, await ReadPlanStatusAsync(vault.VaultId));
        Assert.NotEqual(recordId, newRecordId);
        Assert.Equal(newRecordId, againRecordId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverRecord old = await db.Set<HandoverRecord>().SingleAsync(r => r.Id == recordId);
            Assert.Equal(HandoverRecordStatus.Cancelled, old.Status);
            Assert.NotNull(old.CancelledAt);
        });
    }

    [SqlFact]
    public async Task RejectedRecipient_CanSendANewRequest_EvenAfterTheVaultIsLocked()
    {
        // Chuẩn bị: luật sư từ chối người đầu, chấp nhận người thứ hai nên kho khóa và biên bản vẫn mở
        LockVault vault = await BuildVaultAsync();
        (Guid firstClaim, Guid recordId) = await SubmitOkAsync(vault.FirstClient, vault.VaultId);
        (Guid secondClaim, Guid _) = await SubmitOkAsync(vault.SecondClient, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, recordId, firstClaim, "REJECT", "Thiếu di chúc")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, recordId, secondClaim, "ACCEPT", null)).StatusCode);
        Assert.Equal(PlanStatus.HandoverRequested, await ReadPlanStatusAsync(vault.VaultId));

        // Hành động: xem danh sách kho của người bị từ chối rồi gửi yêu cầu mới
        JsonElement rejectedRow = (await ApiTestHelpers.ReadJsonAsync(await vault.FirstClient.GetAsync(RequestsUrl + "/available")))[0];
        (Guid againClaim, Guid againRecord) = await SubmitOkAsync(vault.FirstClient, vault.VaultId);
        JsonElement pendingRow = (await ApiTestHelpers.ReadJsonAsync(await vault.FirstClient.GetAsync(RequestsUrl + "/available")))[0];
        HttpResponseMessage twice = await vault.FirstClient.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vault.VaultId, "Gửi lần nữa"));

        // Kiểm tra: dòng bị từ chối gửi lại được; sau khi gửi thì chờ xét, không gửi trùng; vẫn một biên bản, một dòng yêu cầu
        Assert.Equal("REJECTED", rejectedRow.GetProperty("status").GetString());
        Assert.True(rejectedRow.GetProperty("canRequest").GetBoolean());
        Assert.Equal(recordId, againRecord);
        Assert.Equal(firstClaim, againClaim);
        Assert.Equal("REQUEST_PENDING", pendingRow.GetProperty("status").GetString());
        Assert.False(pendingRow.GetProperty("canRequest").GetBoolean());
        JsonElement duplicate = await ApiTestHelpers.AssertProblemAsync(twice, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("ALREADY_REQUESTED", duplicate.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverClaim claim = await db.Set<HandoverClaim>().SingleAsync(c => c.Id == firstClaim);
            Assert.Equal(HandoverClaimStatus.Submitted, claim.Status);
            // Lý do từ chối lần trước được giữ lại để luật sư xem khi xét lần gửi mới.
            Assert.Equal("Thiếu di chúc", claim.ReviewReason);
        });
    }

    [SqlFact]
    public async Task OpenRequest_BlocksRemovingTheLastAssignment_AndFinalizeNeedsALockedVault()
    {
        // Chuẩn bị
        LockVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitOkAsync(vault.FirstClient, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        string assignmentsUrl = $"/api/v1/vaults/{vault.VaultId}/assets/";

        // Hành động: Chủ tài sản gỡ hai dòng gán (dòng thứ hai là dòng cuối của kế hoạch)
        HttpResponseMessage removeFirst = await vault.Owner.DeleteAsync(
            assignmentsUrl + vault.AssetA + "/assignments/" + vault.FirstRecipient);
        HttpResponseMessage removeLast = await vault.Owner.DeleteAsync(
            assignmentsUrl + vault.AssetB + "/assignments/" + vault.FirstRecipient);

        // Kiểm tra: gỡ bớt được (kho chưa khóa), nhưng không làm kế hoạch về bản nháp khi đang có yêu cầu
        Assert.True(removeFirst.IsSuccessStatusCode, "remove first -> " + removeFirst.StatusCode);
        JsonElement last = await ApiTestHelpers.AssertProblemAsync(removeLast, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("HANDOVER_REQUEST_PENDING", last.GetProperty("reason").GetString());
        Assert.Equal(PlanStatus.Active, await ReadPlanStatusAsync(vault.VaultId));

        // Hành động: chấp nhận rồi (dữ liệu cũ) kế hoạch bị đưa về Đang hoạt động; luật sư lấy cách chia và chốt
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, recordId, claimId, "ACCEPT", null)).StatusCode);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.Active);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);
        Assert.Equal(HttpStatusCode.OK, (await verifier.Client.PostAsync(url + "/use-owner-split", null)).StatusCode);
        HttpResponseMessage finalize = await verifier.Client.PostAsJsonAsync(url + "/finalize", HandoverRecordTestData.FinalizeBody);

        // Kiểm tra: kho chưa khóa thì chưa chốt được
        JsonElement notLocked = await ApiTestHelpers.AssertProblemAsync(finalize, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("VAULT_NOT_LOCKED", notLocked.GetProperty("reason").GetString());
    }
}
