using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Hồ sơ báo tin qua HTTP trên SQL Server thật: người thân chưa được khai nộp giấy tờ, luật sư xét rồi đi tiếp tới lúc
/// người nhận do luật sư thêm ký biên bản. Kho không khai người nhận nào.
/// </summary>
public sealed class HandoverReportFlowTests : IAsyncLifetime
{
    private const string ReportsUrl = "/api/v1/handover/reports";
    private const string VerifierReportsUrl = "/api/v1/verifier/handover-reports";
    private const string OwnerCitizenId = "001099001234";
    private const string RecipientsJson =
        "[{\"fullName\":\"Trần Thị Con\",\"email\":\"Con@Example.com\",\"phone\":\"0901234567\"}]";

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

    private sealed class NoRecipientVault
    {
        public HttpClient Owner { get; set; } = null!;
        public Guid VaultId { get; set; }
        public Guid AssetId { get; set; }
    }

    /// <summary>Kho đang hoạt động, có một tài sản, có Người thực thi, Chủ kho đã eKYC, không khai người nhận nào.</summary>
    private async Task<NoRecipientVault> BuildVaultAsync(PlanStatus status = PlanStatus.Active)
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        NoRecipientVault result = new NoRecipientVault();
        result.Owner = owner;
        result.VaultId = vaultId;
        result.AssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, status);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOwnerIdentityProtector protector = scope.ServiceProvider.GetRequiredService<IOwnerIdentityProtector>();
            await ApiTestHelpers.WithDbAsync(factory, async db =>
            {
                Guid ownerId = await db.Set<Vault>().Where(v => v.Id == vaultId).Select(v => v.OwnerId).SingleAsync();
                OwnerIdentity identity = new OwnerIdentity();
                identity.UserId = ownerId;
                identity.SourceVerificationId = Guid.NewGuid();
                identity.CreatedAt = DateTimeOffset.UtcNow;
                OwnerIdentityData data = new OwnerIdentityData();
                data.CitizenId = OwnerCitizenId;
                data.FullName = "NGUYỄN VĂN MẤT";
                data.DateOfBirth = new DateOnly(1950, 1, 1);
                protector.Protect(identity, data);
                db.Add(identity);
                await db.SaveChangesAsync();
            });
        }
        return result;
    }

    private static MultipartFormDataContent ReportForm(string citizenId, string? recipients, bool withDeathCertificate = true, bool withWill = true)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent("Nguyễn Văn Mất"), "deceasedFullName");
        form.Add(new StringContent(citizenId), "deceasedCitizenId");
        form.Add(new StringContent("true"), "attested");
        form.Add(new StringContent("Tôi là con của người mất"), "note");
        if (recipients != null)
        {
            form.Add(new StringContent(recipients), "recipients");
        }
        if (withDeathCertificate)
        {
            form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "deathCertificates", "chung-tu.pdf");
        }
        if (withWill)
        {
            form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "wills", "di-chuc.pdf");
        }
        return form;
    }

    private async Task<JsonElement> SubmitOkAsync(HttpClient client, string citizenId)
    {
        HttpResponseMessage response = await client.PostAsync(ReportsUrl, ReportForm(citizenId, RecipientsJson));
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, response.StatusCode + ": " + text);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private static Task<HttpResponseMessage> ReviewAsync(
        HttpClient verifier, Guid reportId, string decision, string? reason = null, bool confirmDocuments = false)
    {
        return verifier.PostAsJsonAsync($"{VerifierReportsUrl}/{reportId}/review", new { decision, reason, confirmDocuments });
    }

    private Task<PlanStatus> ReadPlanStatusAsync(Guid vaultId)
    {
        return ReadAsync(db => db.Set<Vault>().Where(v => v.Id == vaultId).Select(v => v.Plan.Status).SingleAsync());
    }

    private async Task<T> ReadAsync<T>(Func<LegacyVault.Infrastructure.Data.AppDbContext, Task<T>> query)
    {
        T result = default!;
        await ApiTestHelpers.WithDbAsync(factory, async db => result = await query(db));
        return result;
    }

    private Task<HttpClient> NewUserAsync(string? email = null)
    {
        if (email == null)
        {
            return ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        }
        return ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, email);
    }

    [SqlFact]
    public async Task FullFlow_UndeclaredRelativeReports_LawyerAddsRecipient_RecipientSigns()
    {
        NoRecipientVault vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient relative = await NewUserAsync();

        // Nộp hồ sơ: đã tiếp nhận, kho chưa bị khóa, dữ liệu cá nhân không nằm dạng rõ trong DB
        JsonElement mine = await SubmitOkAsync(relative, OwnerCitizenId);
        Guid reportId = mine.GetProperty("id").GetGuid();
        Assert.Equal("SUBMITTED", mine.GetProperty("status").GetString());
        Assert.False(mine.TryGetProperty("vaultFound", out _));
        Assert.Equal(PlanStatus.Active, await ReadPlanStatusAsync(vault.VaultId));
        string stored = await ReadAsync(db => db.Set<HandoverReport>().Select(r => r.DataProtected).SingleAsync());
        Assert.DoesNotContain(OwnerCitizenId, stored);
        Assert.DoesNotContain("con@example.com", stored);

        // Luật sư thấy kho khớp, người nhận gợi ý và tải được giấy tờ
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(VerifierReportsUrl));
        JsonElement seen = list[0];
        Assert.True(seen.GetProperty("vaultFound").GetBoolean());
        Assert.True(seen.GetProperty("nameMatches").GetBoolean());
        Assert.Equal("con@example.com", seen.GetProperty("recipients")[0].GetProperty("email").GetString());
        Assert.Equal("DEATH_CERTIFICATE", seen.GetProperty("files")[0].GetProperty("kind").GetString());
        Guid fileId = seen.GetProperty("files")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await verifier.Client.GetAsync($"{VerifierReportsUrl}/{reportId}/files/{fileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await relative.GetAsync($"{ReportsUrl}/{reportId}/files/{fileId}")).StatusCode);
        HttpClient stranger = await NewUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{ReportsUrl}/{reportId}/files/{fileId}")).StatusCode);

        // Chấp nhận: mở biên bản, khóa kho, Chủ kho thấy ai báo
        HttpResponseMessage accepted = await ReviewAsync(verifier.Client, reportId, "ACCEPT");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Guid recordId = (await ApiTestHelpers.ReadJsonAsync(accepted)).GetProperty("recordId").GetGuid();
        Assert.Equal(PlanStatus.HandoverRequested, await ReadPlanStatusAsync(vault.VaultId));
        JsonElement ownerRequest = await ApiTestHelpers.ReadJsonAsync(
            await vault.Owner.GetAsync($"/api/v1/vaults/{vault.VaultId}/handover-request"));
        Assert.Equal(1, ownerRequest.GetProperty("requesterNames").GetArrayLength());
        await ApiTestHelpers.AssertProblemAsync(
            await ReviewAsync(verifier.Client, reportId, "REJECT", "x"), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");

        // Người nộp không tự thành người nhận
        string recordUrl = HandoverRecordTestData.VerifierRecordUrl(recordId);
        JsonElement record = await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(recordUrl));
        Assert.Equal(0, record.GetProperty("availableRecipients").GetArrayLength());
        Assert.Equal("Trần Thị Con", record.GetProperty("reports")[0].GetProperty("recipients")[0].GetProperty("fullName").GetString());

        // Luật sư thêm người nhận, lập và chốt biên bản (không có yêu cầu nhận nào, chỉ có hồ sơ báo tin)
        HttpResponseMessage added = await verifier.Client.PostAsJsonAsync(recordUrl + "/recipients", new
        {
            fullName = "Trần Thị Con",
            citizenId = "001090000099",
            dateOfBirth = "1990-01-01",
            email = "con@example.com"
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Guid recipientId = (await ApiTestHelpers.ReadJsonAsync(added)).GetProperty("availableRecipients")[0].GetProperty("beneficiaryId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await verifier.Client.PutAsJsonAsync(recordUrl + "/lines",
            new { lines = new[] { new { beneficiaryId = recipientId, assetId = vault.AssetId } } })).StatusCode);
        HttpResponseMessage finalized = await verifier.Client.PostAsJsonAsync(recordUrl + "/finalize", HandoverRecordTestData.FinalizeBody);
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);

        // Ba bên ký xong thì biên bản hoàn tất
        HttpClient recipient = await NewUserAsync("con@example.com");
        JsonElement invitations = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync("/api/v1/handover/invitations/mine"));
        Guid turnId = invitations[0].GetProperty("turnId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await verifier.Client.PostAsync(recordUrl + "/sign", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await executor.Client.PostAsync(
            HandoverRecordTestData.ExecutorRecordUrl(vault.VaultId) + "/sign", null)).StatusCode);
        HttpResponseMessage signed = await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/record/sign", null);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal("COMPLETED", (await ApiTestHelpers.ReadJsonAsync(signed)).GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Submit_GivesSameAnswerWhenNoVaultExists_AndLawyerCannotAcceptIt()
    {
        await BuildVaultAsync();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient first = await NewUserAsync();
        HttpClient second = await NewUserAsync();

        JsonElement found = await SubmitOkAsync(first, OwnerCitizenId);
        JsonElement notFound = await SubmitOkAsync(second, "001099005678");
        List<string> foundFields = found.EnumerateObject().Select(p => p.Name).ToList();
        List<string> notFoundFields = notFound.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(foundFields, notFoundFields);
        Assert.Equal(found.GetProperty("status").GetString(), notFound.GetProperty("status").GetString());

        Guid reportId = notFound.GetProperty("id").GetGuid();
        await ApiTestHelpers.AssertProblemAsync(
            await ReviewAsync(verifier.Client, reportId, "ACCEPT"), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, reportId, "REJECT", "Giấy tờ không hợp lệ")).StatusCode);
        JsonElement mine = await ApiTestHelpers.ReadJsonAsync(await second.GetAsync(ReportsUrl + "/mine"));
        Assert.Equal("REJECTED", mine[0].GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Submit_RejectsBadInput_Duplicates_AndStaffAccounts()
    {
        await BuildVaultAsync();
        HttpClient relative = await NewUserAsync();
        // Chưa có luật sư nào
        await ApiTestHelpers.AssertProblemAsync(
            await relative.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);

        await ApiTestHelpers.AssertProblemAsync(
            await relative.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null, withDeathCertificate: false)),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await relative.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null, withWill: false)),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await relative.PostAsync(ReportsUrl, ReportForm("123", null)), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        string[] badRecipients = new string[]
        {
            "khong phai json", "[null]", "[{\"fullName\":\"A\"}]", "[{\"fullName\":\"A\",\"email\":\"sai\"}]"
        };
        foreach (string bad in badRecipients)
        {
            await ApiTestHelpers.AssertProblemAsync(
                await relative.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, bad)), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        HttpClient unverified = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await verifier.Client.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await relative.GetAsync(VerifierReportsUrl)).StatusCode);
    }

    [SqlFact]
    public async Task Submit_LimitsDuplicatesAndOpenReports()
    {
        NoRecipientVault vault = await BuildVaultAsync();
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        await ApiTestHelpers.AssertProblemAsync(
            await vault.Owner.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        // Nộp trùng và quá ba hồ sơ đang chờ
        HttpClient another = await NewUserAsync();
        await SubmitOkAsync(another, OwnerCitizenId);
        await ApiTestHelpers.AssertProblemAsync(
            await another.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        await SubmitOkAsync(another, "001099000001");
        await SubmitOkAsync(another, "001099000002");
        await ApiTestHelpers.AssertProblemAsync(
            await another.PostAsync(ReportsUrl, ReportForm("001099000003", null)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
    }

    [SqlFact]
    public async Task Supplement_ThenAccept_LocksVault()
    {
        NoRecipientVault vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient relative = await NewUserAsync();
        Guid reportId = (await SubmitOkAsync(relative, OwnerCitizenId)).GetProperty("id").GetGuid();

        MultipartFormDataContent early = new MultipartFormDataContent();
        early.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "files", "them.pdf");
        Assert.Equal(HttpStatusCode.Conflict, (await relative.PostAsync($"{ReportsUrl}/{reportId}/supplement", early)).StatusCode);
        await ApiTestHelpers.AssertProblemAsync(
            await ReviewAsync(verifier.Client, reportId, "REQUEST_SUPPLEMENT"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, reportId, "REQUEST_SUPPLEMENT", "Ảnh mờ")).StatusCode);
        MultipartFormDataContent more = new MultipartFormDataContent();
        more.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "files", "them.pdf");
        HttpResponseMessage supplemented = await relative.PostAsync($"{ReportsUrl}/{reportId}/supplement", more);
        Assert.Equal(HttpStatusCode.OK, supplemented.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(supplemented);
        Assert.Equal("SUBMITTED", body.GetProperty("status").GetString());
        Assert.Equal(3, body.GetProperty("files").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(verifier.Client, reportId, "ACCEPT")).StatusCode);
        Assert.Equal(PlanStatus.HandoverRequested, await ReadPlanStatusAsync(vault.VaultId));
    }

    /// <summary>Form hợp lệ nhưng thay một trường chữ bằng giá trị khác.</summary>
    private static MultipartFormDataContent FormWith(string name, string value)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        Dictionary<string, string> fields = new Dictionary<string, string>();
        fields["deceasedFullName"] = "Nguyễn Văn Mất";
        fields["deceasedCitizenId"] = OwnerCitizenId;
        fields["attested"] = "true";
        fields[name] = value;
        foreach (KeyValuePair<string, string> field in fields)
        {
            form.Add(new StringContent(field.Value), field.Key);
        }
        form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "deathCertificates", "chung-tu.pdf");
        form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "wills", "di-chuc.pdf");
        return form;
    }

    [SqlFact]
    public async Task Submit_ChecksAttestation_AndRecipientContacts()
    {
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient relative = await NewUserAsync();
        MultipartFormDataContent[] bad = new MultipartFormDataContent[]
        {
            FormWith("attested", "false"),
            FormWith("recipients", "[{\"fullName\":\"A\",\"phone\":\"12ab\"}]"),
            FormWith("recipients", "[{\"fullName\":\"A\",\"email\":\"a@x.vn\"},{\"fullName\":\"B\",\"email\":\"A@x.vn\"}]")
        };
        foreach (MultipartFormDataContent form in bad)
        {
            await ApiTestHelpers.AssertProblemAsync(
                await relative.PostAsync(ReportsUrl, form), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        HttpResponseMessage ok = await relative.PostAsync(
            ReportsUrl, FormWith("recipients", "[{\"fullName\":\"A\",\"phone\":\"090 123-45.67\"}]"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(ok);
        Assert.Equal("0901234567", body.GetProperty("recipients")[0].GetProperty("phone").GetString());
    }

    [SqlFact]
    public async Task Accept_WithWarnings_NeedsConfirmationAndReason()
    {
        NoRecipientVault vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient relative = await NewUserAsync();
        // Họ tên khai không khớp họ tên eKYC của Chủ kho thì hồ sơ có cảnh báo.
        HttpResponseMessage submitted = await relative.PostAsync(ReportsUrl, FormWith("deceasedFullName", "Trần Văn Khác"));
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Guid reportId = (await ApiTestHelpers.ReadJsonAsync(submitted)).GetProperty("id").GetGuid();
        JsonElement seen = (await ApiTestHelpers.ReadJsonAsync(await verifier.Client.GetAsync(VerifierReportsUrl)))[0];
        Assert.False(seen.GetProperty("nameMatches").GetBoolean());

        await ApiTestHelpers.AssertProblemAsync(
            await ReviewAsync(verifier.Client, reportId, "ACCEPT", "Đã kiểm"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await ReviewAsync(verifier.Client, reportId, "ACCEPT", null, true), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(PlanStatus.Active, await ReadPlanStatusAsync(vault.VaultId));
        HttpResponseMessage accepted = await ReviewAsync(verifier.Client, reportId, "ACCEPT", "Người nhà dùng máy", true);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Người nhà dùng máy", (await ApiTestHelpers.ReadJsonAsync(accepted)).GetProperty("acceptNote").GetString());
        // Ghi chú nội bộ không lộ cho người nộp.
        JsonElement mine = (await ApiTestHelpers.ReadJsonAsync(await relative.GetAsync(ReportsUrl + "/mine")))[0];
        Assert.False(mine.TryGetProperty("acceptNote", out _));
    }

    [SqlFact]
    public async Task Verifier_CancelsDraftRecord_UnlocksVault_AndRepeatReportsAreLimited()
    {
        NoRecipientVault vault = await BuildVaultAsync();
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient relative = await NewUserAsync();
        Guid reportId = (await SubmitOkAsync(relative, OwnerCitizenId)).GetProperty("id").GetGuid();
        JsonElement accepted = await ApiTestHelpers.ReadJsonAsync(await ReviewAsync(verifier.Client, reportId, "ACCEPT"));
        string cancelUrl = HandoverRecordTestData.VerifierRecordUrl(accepted.GetProperty("recordId").GetGuid()) + "/cancel";

        Assert.Equal(HttpStatusCode.BadRequest, (await verifier.Client.PostAsJsonAsync(cancelUrl, new { reason = " " })).StatusCode);
        VaultTestData.ExecutorClient other = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        if (other.User.Id != verifier.User.Id)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(cancelUrl, new { reason = "x" })).StatusCode);
        }
        HttpResponseMessage cancelled = await verifier.Client.PostAsJsonAsync(cancelUrl, new { reason = "Mở nhầm hồ sơ" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("CANCELLED", (await ApiTestHelpers.ReadJsonAsync(cancelled)).GetProperty("status").GetString());
        Assert.Equal(PlanStatus.Active, await ReadPlanStatusAsync(vault.VaultId));
        Assert.Equal(HttpStatusCode.Conflict, (await verifier.Client.PostAsJsonAsync(cancelUrl, new { reason = "lại" })).StatusCode);
        JsonElement mine = (await ApiTestHelpers.ReadJsonAsync(await relative.GetAsync(ReportsUrl + "/mine")))[0];
        Assert.Equal("Mở nhầm hồ sơ", mine.GetProperty("cancelReason").GetString());

        // Nộp lại được, nhưng tối đa ba hồ sơ cho cùng một người trong 30 ngày.
        for (int i = 0; i < 2; i++)
        {
            Guid again = (await SubmitOkAsync(relative, OwnerCitizenId)).GetProperty("id").GetGuid();
            await ApiTestHelpers.WithDbAsync(factory, async db => await db.Set<HandoverReport>().Where(r => r.Id == again)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, HandoverClaimStatus.Rejected)));
        }
        await ApiTestHelpers.AssertProblemAsync(
            await relative.PostAsync(ReportsUrl, ReportForm(OwnerCitizenId, null)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
    }

    [SqlFact]
    public async Task Accept_IsBlockedWhenPlanIsNotActive()
    {
        NoRecipientVault vault = await BuildVaultAsync(PlanStatus.Draft);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient relative = await NewUserAsync();
        Guid reportId = (await SubmitOkAsync(relative, OwnerCitizenId)).GetProperty("id").GetGuid();
        await ApiTestHelpers.AssertProblemAsync(
            await ReviewAsync(verifier.Client, reportId, "ACCEPT"), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal(PlanStatus.Draft, await ReadPlanStatusAsync(vault.VaultId));
    }
}
