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
/// Biên bản bàn giao qua HTTP trên SQL Server thật: người nhận gửi yêu cầu kèm giấy tờ, luật sư xét, lập, chốt, mở lại,
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

    /// <summary>Kho đã kích hoạt: hai tài sản chữ gán cho người nhận đầu; người nhận thứ hai chưa được gán tài sản nào.</summary>
    private async Task<Vault2> BuildVaultAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetA = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        Guid assetB = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Xe");
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000011", ApiTestHelpers.BeneficiaryName(0), RecipientEmail(0));
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000012", ApiTestHelpers.BeneficiaryName(1), RecipientEmail(1));
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetA, first.GetProperty("id").GetGuid());
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetB, first.GetProperty("id").GetGuid());
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

    /// <summary>Luật sư chốt biên bản kèm tick xác nhận căn cứ pháp lý; phải trả 200.</summary>
    private static async Task<JsonElement> FinalizeOkAsync(HttpClient verifier, string recordUrl)
    {
        HttpResponseMessage response = await verifier.PostAsJsonAsync(recordUrl + "/finalize", HandoverRecordTestData.FinalizeBody);
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, recordUrl + "/finalize -> " + response.StatusCode + ": " + text);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private const string RequestsUrl = "/api/v1/handover/requests";

    /// <summary>GET /handover/requests/available của một tài khoản; phải trả 200.</summary>
    private static async Task<JsonElement> ReadAvailableAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync(RequestsUrl + "/available");
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, "available -> " + response.StatusCode + ": " + text);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    [SqlFact]
    public async Task Available_ListsMyVaultsByVerifiedEmail_AndSubmitByAccountUsesTheSameRules()
    {
        // Chuẩn bị: kho đã kích hoạt có hai người nhận; người thứ hai chưa xác minh email; một người lạ
        Vault2 vault = await BuildVaultAsync();
        await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient first = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail(0));
        HttpClient second = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: false, RecipientEmail(1));
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động: xem danh sách trước khi ai gửi yêu cầu
        JsonElement firstBefore = await ReadAvailableAsync(first);
        JsonElement secondUnverified = await ReadAvailableAsync(second);
        JsonElement strangerList = await ReadAvailableAsync(stranger);
        JsonElement ownerList = await ReadAvailableAsync(vault.Owner);

        // Kiểm tra: chỉ đúng người nhận đã xác minh email thấy kho; không lộ CCCD, gói hay tài sản
        Assert.Equal(1, firstBefore.GetArrayLength());
        JsonElement row = firstBefore[0];
        Assert.Equal(vault.VaultId, row.GetProperty("vaultId").GetGuid());
        Assert.Equal(vault.FirstRecipient, row.GetProperty("beneficiaryId").GetGuid());
        Assert.Equal("NOT_REQUESTED", row.GetProperty("status").GetString());
        Assert.False(row.GetProperty("needsAction").GetBoolean());
        Assert.True(row.GetProperty("canRequest").GetBoolean());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("ownerName").GetString()));
        Assert.False(row.TryGetProperty("citizenId", out JsonElement _));
        Assert.False(row.TryGetProperty("containerName", out JsonElement _));
        Assert.False(row.TryGetProperty("assets", out JsonElement _));
        Assert.Equal(0, secondUnverified.GetArrayLength());
        Assert.Equal(0, strangerList.GetArrayLength());
        Assert.Equal(0, ownerList.GetArrayLength());

        // Hành động: gửi yêu cầu trong app (không cần link)
        HttpResponseMessage strangerSubmit = await stranger.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vault.VaultId, "Tôi là người lạ"));
        HttpResponseMessage unverifiedSubmit = await second.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vault.VaultId, "Chưa xác minh"));
        HttpResponseMessage firstSubmit = await first.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vault.VaultId, "Bố tôi mất"));
        HttpResponseMessage firstAgain = await first.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vault.VaultId, "Gửi lại"));

        // Kiểm tra: người lạ 404, chưa xác minh 403; gửi trùng bị chặn như luồng link
        await ApiTestHelpers.AssertProblemAsync(strangerSubmit, HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(unverifiedSubmit, HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");
        Assert.Equal(HttpStatusCode.OK, firstSubmit.StatusCode);
        JsonElement claim = await ApiTestHelpers.ReadJsonAsync(firstSubmit);
        Guid claimId = claim.GetProperty("id").GetGuid();
        Guid recordId = claim.GetProperty("recordId").GetGuid();
        JsonElement duplicate = await ApiTestHelpers.AssertProblemAsync(firstAgain, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("ALREADY_REQUESTED", duplicate.GetProperty("reason").GetString());
        JsonElement pendingRow = (await ReadAvailableAsync(first))[0];
        Assert.Equal("REQUEST_PENDING", pendingRow.GetProperty("status").GetString());
        Assert.Equal(claimId, pendingRow.GetProperty("claimId").GetGuid());
        // Gửi yêu cầu chưa khóa kho: chỉ khóa khi luật sư chấp nhận giấy tờ.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(PlanStatus.Active, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vault.VaultId)).Status);
        });

        // Hành động: người thứ hai xác minh email; luật sư yêu cầu người đầu bổ sung
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            User secondUser = await db.Users.SingleAsync(u => u.Email == RecipientEmail(1));
            secondUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        });
        JsonElement secondRow = (await ReadAvailableAsync(second))[0];
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        HttpResponseMessage reviewed = await verifier.Client.PostAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/claims/" + claimId + "/review",
            new { decision = "REQUEST_SUPPLEMENT", reason = "Thiếu bản sao di chúc" });
        HttpResponseMessage secondSubmit = await second.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vault.VaultId, "Tôi cũng là người nhận"));

        // Kiểm tra: người khác đã gửi nên người thứ hai cần gửi; người đầu cần nộp bổ sung kèm lý do
        Assert.Equal("NOT_REQUESTED", secondRow.GetProperty("status").GetString());
        Assert.True(secondRow.GetProperty("needsAction").GetBoolean());
        Assert.True(secondRow.GetProperty("canRequest").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondSubmit.StatusCode);
        Assert.Equal(recordId, (await ApiTestHelpers.ReadJsonAsync(secondSubmit)).GetProperty("recordId").GetGuid());
        JsonElement supplementRow = (await ReadAvailableAsync(first))[0];
        Assert.Equal("SUPPLEMENT_REQUESTED", supplementRow.GetProperty("status").GetString());
        Assert.True(supplementRow.GetProperty("needsAction").GetBoolean());
        Assert.Equal("Thiếu bản sao di chúc", supplementRow.GetProperty("reviewReason").GetString());
    }

    /// <summary>Người nhận đầu tiên gửi yêu cầu bằng link riêng; trả client của người đó, token và phản hồi.</summary>
    private async Task<(HttpClient Client, string Token, HttpResponseMessage Response)> SubmitRequestAsync(Guid vaultId)
    {
        (string token, string email, Guid _) = await HandoverRecordTestData.IssueRequestTokenAsync(factory, vaultId);
        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, email);
        HttpResponseMessage response = await recipient.PostAsync(RequestsUrl, HandoverRecordTestData.RequestForm(token, "Bố tôi mất"));
        return (recipient, token, response);
    }

    [SqlFact]
    public async Task Request_CreatesOneRecordAndAssignsAVerifier_WithoutLockingTheVault()
    {
        Vault2 vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);

        // Chưa ai gửi yêu cầu: chưa có biên bản; Người thực thi không còn tự mở bàn giao được.
        HttpResponseMessage none = await executor.Client.GetAsync(HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId));
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);
        HttpResponseMessage open = await executor.Client.PostAsync(HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId), null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, open.StatusCode);

        // Link riêng cho xem tên Chủ kho và email đã che mà không cần đăng nhập.
        (HttpClient recipient, string token, HttpResponseMessage sent) = await SubmitRequestAsync(vault.VaultId);
        HttpClient anonymous = factory.CreateClient();
        JsonElement preview = await ApiTestHelpers.ReadJsonAsync(
            await anonymous.GetAsync(RequestsUrl + "/preview?token=" + Uri.EscapeDataString(token)));
        Assert.Equal(ApiTestHelpers.BeneficiaryName(0), preview.GetProperty("recipientName").GetString());
        Assert.Contains("•", preview.GetProperty("invitedEmailMasked").GetString());

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        JsonElement claim = await ApiTestHelpers.ReadJsonAsync(sent);
        Assert.Equal("SUBMITTED", claim.GetProperty("status").GetString());
        Assert.Single(claim.GetProperty("files").EnumerateArray());

        // Gửi lại lần nữa: đã có yêu cầu.
        HttpResponseMessage again = await recipient.PostAsync(RequestsUrl, HandoverRecordTestData.RequestForm(token, "lần 2"));
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(again, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("ALREADY_REQUESTED", problem.GetProperty("reason").GetString());

        Guid recordId = claim.GetProperty("recordId").GetGuid();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverRecord record = await db.Set<HandoverRecord>().SingleAsync(r => r.VaultId == vault.VaultId);
            Assert.Equal(recordId, record.Id);
            Assert.Equal(UserRole.Verifier, (await db.Users.SingleAsync(u => u.Id == record.VerifierUserId)).Role);
            Assert.Equal(PlanStatus.Active, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vault.VaultId)).Status);
        });

        // Người thực thi thấy biên bản đang lập; luật sư chưa chấp nhận giấy tờ nên kho chưa khóa, Chủ kho vẫn thêm tài sản.
        JsonElement executorView = await ApiTestHelpers.ReadJsonAsync(
            await executor.Client.GetAsync(HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId)));
        Assert.Equal("DRAFTING", executorView.GetProperty("status").GetString());
        Assert.Equal(0, executorView.GetProperty("claims").GetArrayLength());
        Assert.False(executorView.GetProperty("vaultLocked").GetBoolean());
        HttpResponseMessage stillEditable = await vault.Owner.PostAsJsonAsync(
            $"/api/v1/vaults/{vault.VaultId}/assets/text", new { name = "Mới", content = "x" });
        Assert.Equal(HttpStatusCode.Created, stillEditable.StatusCode);
    }

    [SqlFact]
    public async Task Request_BadLink_WrongEmail_AndWithoutVerifier_AreRejected()
    {
        Vault2 vault = await BuildVaultAsync();
        (string token, string _, Guid _) = await HandoverRecordTestData.IssueRequestTokenAsync(factory, vault.VaultId);

        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        HttpResponseMessage badLink = await stranger.PostAsync(RequestsUrl, HandoverRecordTestData.RequestForm("sai-link", "x"));
        await ApiTestHelpers.AssertProblemAsync(badLink, HttpStatusCode.NotFound, "HANDOVER_INVITATION_INVALID");
        HttpResponseMessage wrongEmail = await stranger.PostAsync(RequestsUrl, HandoverRecordTestData.RequestForm(token, "x"));
        await ApiTestHelpers.AssertProblemAsync(wrongEmail, HttpStatusCode.Forbidden, "HANDOVER_EMAIL_MISMATCH");

        // Không còn luật sư nào đang hoạt động (database tạm dùng chung trong lớp nên tắt hết luật sư của test khác).
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Role == UserRole.Verifier)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, UserStatus.Disabled));
        });
        (HttpClient _, string _, HttpResponseMessage noVerifier) = await SubmitRequestAsync(vault.VaultId);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(noVerifier, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("NO_VERIFIER", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.False(await db.Set<HandoverRecord>().AnyAsync(r => r.VaultId == vault.VaultId));
            Assert.False(await db.Set<HandoverRecordFile>().AnyAsync());
        });
    }

    [SqlFact]
    public async Task Owner_ViewsRequest_VerifierCancelUnlocksVaultAndRecipientCanRequestAgain()
    {
        Vault2 vault = await BuildVaultAsync();
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        (HttpClient recipient, string token, HttpResponseMessage sent) = await SubmitRequestAsync(vault.VaultId);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        string ownerUrl = $"/api/v1/vaults/{vault.VaultId}/handover-request";

        JsonElement request = await ApiTestHelpers.ReadJsonAsync(await vault.Owner.GetAsync(ownerUrl));
        Assert.Equal(ApiTestHelpers.BeneficiaryName(0), request.GetProperty("requesterNames")[0].GetString());

        // Người nhận không xem được yêu cầu của kho.
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync(ownerUrl)).StatusCode);

        // Luật sư hủy biên bản đang lập thì kho mở khóa.
        Guid recordId = request.GetProperty("recordId").GetGuid();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        HttpResponseMessage cancelled = await verifier.Client.PostAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/cancel", new { reason = "Mở nhầm hồ sơ" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await vault.Owner.GetAsync(ownerUrl)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(PlanStatus.Active, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vault.VaultId)).Status);
            Assert.Equal(HandoverRecordStatus.Cancelled, (await db.Set<HandoverRecord>().SingleAsync(r => r.VaultId == vault.VaultId)).Status);
        });

        // Kho mở khóa; người nhận gửi lại được và tạo biên bản mới.
        HttpResponseMessage unlocked = await vault.Owner.PostAsJsonAsync(
            $"/api/v1/vaults/{vault.VaultId}/assets/text", new { name = "Mới", content = "x" });
        Assert.Equal(HttpStatusCode.Created, unlocked.StatusCode);
        HttpResponseMessage again = await recipient.PostAsync(RequestsUrl, HandoverRecordTestData.RequestForm(token, "lần 2"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync(RequestsUrl + "/mine"))).GetArrayLength());
    }

    [SqlFact]
    public async Task Verifier_ReviewsDocuments_SupplementRejectAndFinalizeNeedsAllReviewed()
    {
        Vault2 vault = await BuildVaultAsync();
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        (HttpClient recipient, string _, HttpResponseMessage sent) = await SubmitRequestAsync(vault.VaultId);
        JsonElement mine = await ApiTestHelpers.ReadJsonAsync(sent);
        Guid recordId = mine.GetProperty("recordId").GetGuid();
        Guid claimId = mine.GetProperty("id").GetGuid();
        Guid fileId = mine.GetProperty("files")[0].GetProperty("id").GetGuid();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);

        // Luật sư thấy yêu cầu kèm tệp và tải được tệp; người nhận tải lại được tệp của mình.
        JsonElement record = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(url));
        Assert.Equal("SUBMITTED", record.GetProperty("claims")[0].GetProperty("status").GetString());
        HttpResponseMessage file = await verifier.Client.GetAsync(url + "/files/" + fileId);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(HandoverRecordTestData.SamplePdf(), await file.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await recipient.GetAsync(RequestsUrl + "/" + claimId + "/files/" + fileId)).StatusCode);

        // Chưa xét thì chưa chốt được.
        Assert.Equal(HttpStatusCode.OK, (await SetLinesAsync(verifier.Client, recordId, (vault.FirstRecipient, vault.AssetA))).StatusCode);
        JsonElement pending = await ApiTestHelpers.AssertProblemAsync(
            await verifier.Client.PostAsJsonAsync(url + "/finalize", HandoverRecordTestData.FinalizeBody), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("CLAIMS_PENDING", pending.GetProperty("reason").GetString());

        // Bổ sung phải có lý do; người nhận nộp thêm thì quay về chờ xét.
        string review = url + "/claims/" + claimId + "/review";
        await ApiTestHelpers.AssertProblemAsync(
            await verifier.Client.PostAsJsonAsync(review, new { decision = "REQUEST_SUPPLEMENT" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.OK,
            (await verifier.Client.PostAsJsonAsync(review, new { decision = "REQUEST_SUPPLEMENT", reason = "Thiếu giấy chứng tử" })).StatusCode);
        JsonElement asked = (await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync(RequestsUrl + "/mine")))[0];
        Assert.Equal("SUPPLEMENT_REQUESTED", asked.GetProperty("status").GetString());
        Assert.Equal("Thiếu giấy chứng tử", asked.GetProperty("reviewReason").GetString());
        MultipartFormDataContent more = HandoverRecordTestData.RequestForm("bo-qua", "Đã bổ sung");
        more.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "files", "bo-sung.pdf");
        HttpResponseMessage supplemented = await recipient.PostAsync(RequestsUrl + "/" + claimId + "/supplement", more);
        Assert.Equal(HttpStatusCode.OK, supplemented.StatusCode);
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(supplemented);
        Assert.Equal("SUBMITTED", after.GetProperty("status").GetString());
        Assert.Equal(2, after.GetProperty("files").GetArrayLength());

        // Từ chối yêu cầu duy nhất khi kho chưa khóa: người này ra khỏi biên bản và biên bản tự đóng.
        // (Trường hợp kho đã khóa, người bị từ chối không ghi tài sản được nữa: xem HandoverVaultLockFlowTests.)
        Assert.Equal(HttpStatusCode.OK,
            (await verifier.Client.PostAsJsonAsync(review, new { decision = "REJECT", reason = "Giấy tờ không hợp lệ" })).StatusCode);
        JsonElement rejected = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(url));
        Assert.Equal(0, rejected.GetProperty("recipients").GetArrayLength());
        Assert.Equal("CANCELLED", rejected.GetProperty("status").GetString());
        JsonElement closed = await ApiTestHelpers.AssertProblemAsync(
            await SetLinesAsync(verifier.Client, recordId, (vault.FirstRecipient, vault.AssetA)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("WRONG_STATE", closed.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Verifier_AddsNewRecipientAndEvidenceFiles()
    {
        Vault2 vault = await BuildVaultAsync();
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);

        // Người nhận mới (có tên trong di chúc nhưng Chủ kho chưa khai): thêm vào kho rồi ghi tài sản được.
        HttpResponseMessage added = await verifier.Client.PostAsJsonAsync(url + "/recipients", new
        {
            fullName = "Lê Văn Mới",
            citizenId = "001090000099",
            dateOfBirth = "1990-01-01",
            email = "nguoi-moi@example.com"
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(added);
        JsonElement person = body.GetProperty("availableRecipients").EnumerateArray()
            .Single(p => p.GetProperty("fullName").GetString() == "Lê Văn Mới");
        Guid newRecipient = person.GetProperty("beneficiaryId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await SetLinesAsync(verifier.Client, recordId, (newRecipient, vault.AssetA))).StatusCode);
        // Trùng CCCD người đã có thì báo lỗi.
        await ApiTestHelpers.AssertProblemAsync(await verifier.Client.PostAsJsonAsync(url + "/recipients", new
        {
            fullName = "Trùng",
            citizenId = "001090000099",
            dateOfBirth = "1990-01-01",
            email = "trung@example.com"
        }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        // Tệp căn cứ: đính kèm, tải, gỡ; tệp không phải PDF hay ảnh thì bị từ chối.
        MultipartFormDataContent evidence = new MultipartFormDataContent();
        ByteArrayContent pdf = new ByteArrayContent(HandoverRecordTestData.SamplePdf());
        evidence.Add(pdf, "files", "di-chuc.pdf");
        JsonElement withEvidence = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.PostAsync(url + "/evidence", evidence));
        Guid evidenceId = withEvidence.GetProperty("evidenceFiles")[0].GetProperty("id").GetGuid();
        Assert.Equal("di-chuc.pdf", withEvidence.GetProperty("evidenceFiles")[0].GetProperty("fileName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await verifier.Client.GetAsync(url + "/files/" + evidenceId)).StatusCode);
        MultipartFormDataContent bad = new MultipartFormDataContent();
        bad.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("không phải pdf")), "files", "x.txt");
        await ApiTestHelpers.AssertProblemAsync(await verifier.Client.PostAsync(url + "/evidence", bad), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        JsonElement removed = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.DeleteAsync(url + "/evidence/" + evidenceId));
        Assert.Equal(0, removed.GetProperty("evidenceFiles").GetArrayLength());
    }

    [SqlFact]
    public async Task Verifier_AddsRecipientsInOneBatch_AllOrNothing()
    {
        // Chuẩn bị: biên bản đang lập; kho đã có người nhận do Chủ kho khai
        Vault2 vault = await BuildVaultAsync();
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);
        int before = (await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(url)))
            .GetProperty("availableRecipients").GetArrayLength();
        object first = new
        {
            fullName = "Lê Văn Một",
            citizenId = "001090000097",
            dateOfBirth = "1990-01-01",
            email = "mot@example.com"
        };
        object sameEmail = new
        {
            fullName = "Lê Văn Hai",
            citizenId = "001090000098",
            dateOfBirth = "1990-01-01",
            email = "MOT@example.com"
        };
        object second = new
        {
            fullName = "Lê Văn Hai",
            citizenId = "001090000098",
            dateOfBirth = "1990-01-01",
            email = "hai@example.com"
        };

        // Hành động: người thứ hai trùng email người thứ nhất
        HttpResponseMessage rejected = await verifier.Client.PostAsJsonAsync(
            url + "/recipients/batch", new { recipients = new[] { first, sameEmail } });

        // Kiểm tra: báo đúng người, đúng trường; không ai được thêm
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(rejected, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("recipients[1].email", out JsonElement messages));
        Assert.StartsWith("Người nhận mới 2:", messages[0].GetString());
        JsonElement unchanged = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(url));
        Assert.Equal(before, unchanged.GetProperty("availableRecipients").GetArrayLength());

        // Hành động: gửi lại danh sách hợp lệ
        HttpResponseMessage added = await verifier.Client.PostAsJsonAsync(
            url + "/recipients/batch", new { recipients = new[] { first, second } });

        // Kiểm tra: đủ hai người, có email để FE nhận ra
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        JsonElement people = (await ApiTestHelpers.ReadJsonAsync(added)).GetProperty("availableRecipients");
        Assert.Equal(before + 2, people.GetArrayLength());
        List<string?> emails = new List<string?>();
        foreach (JsonElement person in people.EnumerateArray())
        {
            emails.Add(person.GetProperty("email").GetString());
        }
        Assert.Contains("mot@example.com", emails);
        Assert.Contains("hai@example.com", emails);
        // Danh sách trống bị chặn ngay ở bước kiểm dữ liệu gửi lên.
        HttpResponseMessage empty = await verifier.Client.PostAsJsonAsync(
            url + "/recipients/batch", new { recipients = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
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
        Guid openedId = await HandoverRecordTestData.RequestAsync(factory, vault.VaultId);
        HttpResponseMessage drafting = await executor.Client.GetAsync(assetsUrl);
        Assert.Equal(HttpStatusCode.Conflict, drafting.StatusCode);

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
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);

        // Bước 1 chưa có dòng: chưa chốt được; chưa tới bước ký thì không ai ký được.
        await ApiTestHelpers.AssertProblemAsync(await verifier.Client.PostAsJsonAsync(url + "/finalize", HandoverRecordTestData.FinalizeBody), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
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
        await HandoverRecordTestData.AcceptAllClaimsAsync(verifier.Client, recordId);
        // Chưa tick xác nhận căn cứ pháp lý (thiếu hoặc false) thì 400, biên bản vẫn đang lập.
        JsonElement missing = await ApiTestHelpers.AssertProblemAsync(
            await verifier.Client.PostAsync(url + "/finalize", null), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(missing.GetProperty("errors").TryGetProperty("confirmLegalBasis", out JsonElement _));
        await ApiTestHelpers.AssertProblemAsync(
            await verifier.Client.PostAsJsonAsync(url + "/finalize", new { confirmLegalBasis = false }),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        JsonElement stillDrafting = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(url));
        Assert.Equal("DRAFTING", stillDrafting.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, stillDrafting.GetProperty("legalBasisConfirmedAt").ValueKind);
        JsonElement finalized = await FinalizeOkAsync(verifier.Client, url);

        // Chốt xong là biên bản đang giao: luật sư ký được ngay, không chờ danh tính người nhận.
        Assert.Equal("SIGNING", finalized.GetProperty("status").GetString());
        Assert.True(finalized.GetProperty("canSign").GetBoolean());
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
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vault.VaultId);
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
        Assert.Equal("SIGNING", record.Body.GetProperty("status").GetString());
        // Chốt kèm tick căn cứ pháp lý: lưu luật sư và thời điểm.
        Assert.Equal(JsonValueKind.String, record.Body.GetProperty("legalBasisConfirmedAt").ValueKind);
        Assert.Equal(record.Verifier.User.Id, record.Body.GetProperty("legalBasisConfirmedByUserId").GetGuid());
        Assert.False(string.IsNullOrEmpty(record.Body.GetProperty("legalBasisConfirmedByName").GetString()));
        string verifierUrl = HandoverRecordTestData.VerifierRecordUrl(record.RecordId);

        // Người nhận mở link và nhận lượt; danh tính của người này xong thì người này ký được.
        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail(0));
        JsonElement invitations = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync("/api/v1/handover/invitations/mine"));
        Guid turnId = invitations[0].GetProperty("turnId").GetGuid();
        // Biên bản chờ danh tính, chưa nộp ảnh căn cước: mục "Nhận tài sản" nhắc người nhận nộp.
        JsonElement identityRow = (await ReadAvailableAsync(recipient))[0];
        Assert.Equal("NEED_IDENTITY", identityRow.GetProperty("status").GetString());
        Assert.True(identityRow.GetProperty("needsAction").GetBoolean());
        Assert.Equal(turnId, identityRow.GetProperty("turnId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null)).StatusCode);
        JsonElement view = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync($"/api/v1/handover/turns/{turnId}/record"));
        Assert.Equal("SIGNING", view.GetProperty("status").GetString());
        Assert.True(view.GetProperty("canSign").GetBoolean());
        Assert.Equal(1, view.GetProperty("recipients").GetArrayLength());
        // Mục "Nhận tài sản": tới bước ký thì người nhận cần ký, kèm lượt để vào cổng người nhận.
        JsonElement signingRow = (await ReadAvailableAsync(recipient))[0];
        Assert.Equal("NEED_MY_SIGNATURE", signingRow.GetProperty("status").GetString());
        Assert.True(signingRow.GetProperty("needsAction").GetBoolean());
        Assert.Equal(turnId, signingRow.GetProperty("turnId").GetGuid());

        // Luật sư ký rồi mở lại: chữ ký cũ mất hiệu lực, bản mới.
        await PostOkAsync(record.Verifier.Client, verifierUrl + "/sign");
        JsonElement reopened = await PostOkAsync(record.Verifier.Client, verifierUrl + "/reopen");
        Assert.Equal("DRAFTING", reopened.GetProperty("status").GetString());
        Assert.Equal(2, reopened.GetProperty("revisionNo").GetInt32());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("verifierSignedAt").ValueKind);
        // Mở lại thì xác nhận căn cứ pháp lý mất hiệu lực: phải tick lại mới chốt được.
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("legalBasisConfirmedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("legalBasisConfirmedByUserId").ValueKind);
        await ApiTestHelpers.AssertProblemAsync(
            await record.Verifier.Client.PostAsync(verifierUrl + "/finalize", null), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        // Chốt lại: người nhận đã được xác nhận danh tính thì sang bước ký ngay.
        JsonElement refinalized = await FinalizeOkAsync(record.Verifier.Client, verifierUrl);
        Assert.Equal(JsonValueKind.String, refinalized.GetProperty("legalBasisConfirmedAt").ValueKind);
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
        // Đủ chữ ký: người nhận nhập mã và tải; bấm "Tôi đã nhận đủ" thì xong.
        JsonElement readyRow = (await ReadAvailableAsync(recipient))[0];
        Assert.Equal("CODE_READY", readyRow.GetProperty("status").GetString());
        Assert.True(readyRow.GetProperty("needsAction").GetBoolean());
        // "Tôi đã nhận đủ" cần tải hết từng mục; ở đây ghi thẳng mốc nhận đủ để kiểm trạng thái RECEIVED.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverTurn receivedTurn = await db.Set<HandoverTurn>().SingleAsync(t => t.Id == turnId);
            receivedTurn.ReceivedAllAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        });
        JsonElement receivedRow = (await ReadAvailableAsync(recipient))[0];
        Assert.Equal("RECEIVED", receivedRow.GetProperty("status").GetString());
        Assert.False(receivedRow.GetProperty("needsAction").GetBoolean());

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
        await FinalizeOkAsync(record.Verifier.Client, url);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverTurn> turns = await db.Set<HandoverTurn>().Where(t => t.RecordId == record.RecordId).ToListAsync();
            Assert.Equal(HandoverTurnStatus.Cancelled, turns.Single(t => t.BeneficiaryId == vault.FirstRecipient).Status);
            Assert.Equal(HandoverTurnStatus.Invited, turns.Single(t => t.BeneficiaryId == vault.SecondRecipient).Status);
        });
    }
}
