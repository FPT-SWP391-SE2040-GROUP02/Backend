using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Biên bản khác cách chia ban đầu của Chủ kho (SQL Server thật): response liệt kê khác biệt, luật sư phải tick xác nhận khi
/// chốt; không khác thì chốt như cũ; mở lại thì phải tick lại.
/// </summary>
public sealed class HandoverSplitDeviationFlowTests : IAsyncLifetime
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

    private sealed class SplitVault
    {
        public Guid VaultId { get; set; }
        public Guid AssetA { get; set; }
        public Guid AssetB { get; set; }
        public Guid FirstRecipient { get; set; }
        public Guid SecondRecipient { get; set; }
        public Guid RecordId { get; set; }
        public HttpClient Verifier { get; set; } = null!;
        public string RecordUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Kho đã kích hoạt: hai tài sản gán cho người nhận đầu, người nhận thứ hai chưa được gán. Người nhận đầu gửi yêu cầu,
    /// luật sư chấp nhận (kho khóa) và lấy cách chia của Chủ kho làm biên bản.
    /// </summary>
    private async Task<SplitVault> BuildDraftFromOwnerSplitAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetA = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        Guid assetB = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Xe");
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000031", ApiTestHelpers.BeneficiaryName(0), "lech-" + vaultId.ToString("N").Substring(0, 8) + "-0@example.com");
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000032", ApiTestHelpers.BeneficiaryName(1), "lech-" + vaultId.ToString("N").Substring(0, 8) + "-1@example.com");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetA, first.GetProperty("id").GetGuid());
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetB, first.GetProperty("id").GetGuid());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        await HandoverRecordTestData.AcceptAllClaimsAsync(verifier.Client, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);
        Assert.Equal(HttpStatusCode.OK, (await verifier.Client.PostAsync(url + "/use-owner-split", null)).StatusCode);

        SplitVault result = new SplitVault();
        result.VaultId = vaultId;
        result.AssetA = assetA;
        result.AssetB = assetB;
        result.FirstRecipient = first.GetProperty("id").GetGuid();
        result.SecondRecipient = second.GetProperty("id").GetGuid();
        result.RecordId = recordId;
        result.Verifier = verifier.Client;
        result.RecordUrl = url;
        return result;
    }

    private static Task<HttpResponseMessage> FinalizeAsync(SplitVault vault, bool confirmDeviation)
    {
        if (confirmDeviation)
        {
            return vault.Verifier.PostAsJsonAsync(vault.RecordUrl + "/finalize",
                new { confirmLegalBasis = true, confirmSplitDeviation = true });
        }
        return vault.Verifier.PostAsJsonAsync(vault.RecordUrl + "/finalize", new { confirmLegalBasis = true });
    }

    [SqlFact]
    public async Task SameAsOwnerSplit_FinalizesWithoutTheDeviationTick()
    {
        // Chuẩn bị
        SplitVault vault = await BuildDraftFromOwnerSplitAsync();

        // Hành động
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(await vault.Verifier.GetAsync(vault.RecordUrl));
        HttpResponseMessage finalized = await FinalizeAsync(vault, false);

        // Kiểm tra
        Assert.Equal(0, draft.GetProperty("splitDifferences").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(finalized);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("splitDeviationConfirmedAt").ValueKind);
    }

    [SqlFact]
    public async Task AddedRecipient_NeedsTheDeviationTick_AndReopenNeedsItAgain()
    {
        // Chuẩn bị: luật sư cho người thứ hai cùng nhận xe
        SplitVault vault = await BuildDraftFromOwnerSplitAsync();
        List<object> lines = new List<object>();
        lines.Add(new { beneficiaryId = vault.FirstRecipient, assetId = vault.AssetA });
        lines.Add(new { beneficiaryId = vault.FirstRecipient, assetId = vault.AssetB });
        lines.Add(new { beneficiaryId = vault.SecondRecipient, assetId = vault.AssetB });
        HttpResponseMessage set = await vault.Verifier.PutAsJsonAsync(vault.RecordUrl + "/lines", new { lines });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        // Hành động: chốt không tick, rồi tick
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(set);
        HttpResponseMessage withoutTick = await FinalizeAsync(vault, false);
        HttpResponseMessage withTick = await FinalizeAsync(vault, true);

        // Kiểm tra: liệt kê người được thêm và tài sản đổi người nhận (không có CCCD); không tick thì 400
        JsonElement differences = draft.GetProperty("splitDifferences");
        Assert.Equal(2, differences.GetArrayLength());
        Assert.Equal("RECIPIENT_ADDED", differences[0].GetProperty("kind").GetString());
        Assert.Equal(vault.SecondRecipient, differences[0].GetProperty("beneficiaryId").GetGuid());
        Assert.Equal(ApiTestHelpers.BeneficiaryName(1), differences[0].GetProperty("recipientName").GetString());
        Assert.False(differences[0].TryGetProperty("citizenId", out JsonElement _));
        Assert.Equal("ASSET_RECIPIENTS_CHANGED", differences[1].GetProperty("kind").GetString());
        Assert.Equal("Xe", differences[1].GetProperty("assetName").GetString());
        Assert.Equal(1, differences[1].GetProperty("ownerRecipientNames").GetArrayLength());
        Assert.Equal(2, differences[1].GetProperty("recordRecipientNames").GetArrayLength());
        JsonElement missing = await ApiTestHelpers.AssertProblemAsync(withoutTick, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(missing.GetProperty("errors").TryGetProperty("confirmSplitDeviation", out JsonElement _));
        Assert.Equal(HttpStatusCode.OK, withTick.StatusCode);
        JsonElement finalized = await ApiTestHelpers.ReadJsonAsync(withTick);
        Assert.Equal(JsonValueKind.String, finalized.GetProperty("splitDeviationConfirmedAt").ValueKind);
        Assert.False(string.IsNullOrEmpty(finalized.GetProperty("splitDeviationConfirmedByName").GetString()));

        // Hành động: mở lại rồi chốt không tick
        HttpResponseMessage reopened = await vault.Verifier.PostAsync(vault.RecordUrl + "/reopen", null);
        HttpResponseMessage againWithoutTick = await FinalizeAsync(vault, false);

        // Kiểm tra: mở lại xóa xác nhận, phải tick lại
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        JsonElement reopenedBody = await ApiTestHelpers.ReadJsonAsync(reopened);
        Assert.Equal(JsonValueKind.Null, reopenedBody.GetProperty("splitDeviationConfirmedAt").ValueKind);
        await ApiTestHelpers.AssertProblemAsync(againWithoutTick, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.OK, (await FinalizeAsync(vault, true)).StatusCode);
    }
}
