using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// 3 thông tin trên giấy chứng tử (ngày mất, ngày đăng ký khai tử, nơi đăng ký) trên SQL Server thật:
/// luật kiểm khi sửa nháp và gửi, hiển thị cho Người xét duyệt kèm cảnh báo, biên bản của người nhận, API danh mục hành chính
/// (DEATH-01, DEATH-04, DEATH-10).
/// </summary>
public sealed class DeathCaseCertificateInfoFlowTests : IAsyncLifetime
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

    /// <summary>PUT .../draft với body tự soạn; luôn kèm etag hiện tại của hồ sơ.</summary>
    private static async Task<HttpResponseMessage> PutDraftAsync(HttpClient client, JsonElement caseBody, Dictionary<string, object?> fields)
    {
        Guid caseId = caseBody.GetProperty("id").GetGuid();
        Dictionary<string, object?> body = new Dictionary<string, object?>(fields);
        body["etag"] = caseBody.GetProperty("etag").GetString();
        return await client.PutAsJsonAsync(DeathCaseApi.CaseUrl(caseId) + "/draft", body);
    }

    private static string ErrorOf(JsonElement problem, string field)
    {
        return problem.GetProperty("errors").GetProperty(field)[0].GetString()!;
    }

    /// <summary>Kho có kế hoạch đang chạy, Executor và hồ sơ vừa mở (còn trống).</summary>
    private sealed class OpenedCase
    {
        public DeathCaseTestData.OwnerVault Vault { get; set; } = null!;

        public DeathCaseTestData.ExecutorClient Executor { get; set; } = null!;

        public JsonElement Opened { get; set; }
    }

    private async Task<OpenedCase> OpenCaseAsync()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        OpenedCase result = new OpenedCase();
        result.Vault = vault;
        result.Executor = executor;
        result.Opened = opened;
        return result;
    }

    [SqlFact]
    public async Task Death01_DraftWithFutureDeathDateIsRejectedOnThatField()
    {
        // Chuẩn bị
        OpenedCase setup = await OpenCaseAsync();
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["dateOfDeath"] = DeathCaseApi.Format(DeathCaseApi.Today().AddDays(1));

        // Hành động
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, setup.Opened, fields);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("Ngày mất không được sau hôm nay.", ErrorOf(problem, "dateOfDeath"));
    }

    [SqlFact]
    public async Task Death01_DraftWithRegistrationBeforeDeathIsRejectedOnIssuedOn()
    {
        // Chuẩn bị
        OpenedCase setup = await OpenCaseAsync();
        DateOnly death = DeathCaseApi.Today().AddDays(-3);
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["dateOfDeath"] = DeathCaseApi.Format(death);
        fields["issuedOn"] = DeathCaseApi.Format(death.AddDays(-1));

        // Hành động
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, setup.Opened, fields);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("Ngày đăng ký khai tử phải từ ngày mất trở đi.", ErrorOf(problem, "issuedOn"));
    }

    [SqlFact]
    public async Task Death01_DraftWithDeathDateBeforePlanActivationIsRejected()
    {
        // Chuẩn bị: kế hoạch kích hoạt cách đây 1 ngày, ngày mất khai cách đây 3 ngày
        OpenedCase setup = await OpenCaseAsync();
        DateTimeOffset activatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.HandoverPlans.Where(p => p.VaultId == setup.Vault.VaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ActivatedAt, (DateTimeOffset?)activatedAt));
        });
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["dateOfDeath"] = DeathCaseApi.Format(DeathCaseApi.Today().AddDays(-3));

        // Hành động
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, setup.Opened, fields);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        string message = ErrorOf(problem, "dateOfDeath");
        Assert.StartsWith("Ngày mất phải sau ngày Chủ kho kích hoạt kế hoạch (", message);
    }

    [SqlFact]
    public async Task Death01_DraftWithWardOfAnotherProvinceIsRejected()
    {
        // Chuẩn bị: 00004 là phường của Hà Nội, nhưng tỉnh đã chọn là TP.HCM
        OpenedCase setup = await OpenCaseAsync();
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["deathRegistrationProvinceCode"] = DeathCaseApi.ProvinceCode;
        fields["deathRegistrationWardCode"] = "00004";

        // Hành động
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, setup.Opened, fields);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("Phường/xã không thuộc tỉnh/thành phố đã chọn.", ErrorOf(problem, "deathRegistrationWardCode"));
    }

    [SqlFact]
    public async Task Death01_DraftWithUnknownProvinceIsRejected()
    {
        // Chuẩn bị
        OpenedCase setup = await OpenCaseAsync();
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["deathRegistrationProvinceCode"] = "99";

        // Hành động
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, setup.Opened, fields);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("Tỉnh/thành phố không hợp lệ.", ErrorOf(problem, "deathRegistrationProvinceCode"));
    }

    [SqlFact]
    public async Task Death01_DraftSavesTheThreeFactsAndReturnsThePlaceSentence()
    {
        // Chuẩn bị
        OpenedCase setup = await OpenCaseAsync();
        DateOnly death = DeathCaseApi.Today().AddDays(-3);
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["dateOfDeath"] = DeathCaseApi.Format(death);
        fields["issuedOn"] = DeathCaseApi.Format(death.AddDays(1));
        fields["deathRegistrationProvinceCode"] = DeathCaseApi.ProvinceCode;
        fields["deathRegistrationWardCode"] = DeathCaseApi.WardCode;

        // Hành động
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, setup.Opened, fields);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement version = body.GetProperty("currentVersion");
        Assert.Equal(DeathCaseApi.Format(death), version.GetProperty("dateOfDeath").GetString());
        Assert.Equal(DeathCaseApi.Format(death.AddDays(1)), version.GetProperty("issuedOn").GetString());
        Assert.Equal("79", version.GetProperty("deathRegistrationProvinceCode").GetString());
        Assert.Equal("26743", version.GetProperty("deathRegistrationWardCode").GetString());
        Assert.Equal(DeathCaseApi.RegistrationPlace, version.GetProperty("deathRegistrationPlace").GetString());
        Assert.NotEqual(setup.Opened.GetProperty("etag").GetString(), body.GetProperty("etag").GetString());
    }

    [SqlFact]
    public async Task Death01_ChangingProvinceWithoutWardClearsTheWard()
    {
        // Chuẩn bị: đã chọn TP.HCM + Phường Bến Thành
        OpenedCase setup = await OpenCaseAsync();
        Dictionary<string, object?> first = new Dictionary<string, object?>();
        first["deathRegistrationProvinceCode"] = DeathCaseApi.ProvinceCode;
        first["deathRegistrationWardCode"] = DeathCaseApi.WardCode;
        HttpResponseMessage firstResponse = await PutDraftAsync(setup.Executor.Client, setup.Opened, first);
        JsonElement saved = await ApiTestHelpers.ReadJsonAsync(firstResponse);

        // Hành động: đổi sang Hà Nội, không gửi phường
        Dictionary<string, object?> change = new Dictionary<string, object?>();
        change["deathRegistrationProvinceCode"] = "01";
        HttpResponseMessage response = await PutDraftAsync(setup.Executor.Client, saved, change);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement version = body.GetProperty("currentVersion");
        Assert.Equal("01", version.GetProperty("deathRegistrationProvinceCode").GetString());
        Assert.Equal(JsonValueKind.Null, version.GetProperty("deathRegistrationWardCode").ValueKind);
        Assert.Equal(string.Empty, version.GetProperty("deathRegistrationPlace").GetString());
        Assert.Contains("DEATH_REGISTRATION_PLACE", DeathCaseApi.ReadStrings(body.GetProperty("missingRequirements")));
    }

    [SqlFact]
    public async Task Death01_OnlyChangingThePlaceStillBumpsTheEtag()
    {
        // Chuẩn bị
        OpenedCase setup = await OpenCaseAsync();
        Dictionary<string, object?> first = new Dictionary<string, object?>();
        first["deathRegistrationProvinceCode"] = DeathCaseApi.ProvinceCode;
        first["deathRegistrationWardCode"] = DeathCaseApi.WardCode;
        JsonElement saved = await ApiTestHelpers.ReadJsonAsync(await PutDraftAsync(setup.Executor.Client, setup.Opened, first));

        // Hành động: gửi lại đúng giá trị cũ rồi đổi sang phường khác của cùng tỉnh
        JsonElement same = await ApiTestHelpers.ReadJsonAsync(await PutDraftAsync(setup.Executor.Client, saved, first));
        Dictionary<string, object?> other = new Dictionary<string, object?>();
        other["deathRegistrationWardCode"] = "26740";
        JsonElement changed = await ApiTestHelpers.ReadJsonAsync(await PutDraftAsync(setup.Executor.Client, same, other));

        // Kiểm tra: không đổi gì thì etag giữ nguyên; đổi phường thì etag đổi
        Assert.Equal(saved.GetProperty("etag").GetString(), same.GetProperty("etag").GetString());
        Assert.NotEqual(same.GetProperty("etag").GetString(), changed.GetProperty("etag").GetString());
        Assert.Equal("UBND phường Sài Gòn, Thành phố Hồ Chí Minh",
            changed.GetProperty("currentVersion").GetProperty("deathRegistrationPlace").GetString());
    }

    [SqlFact]
    public async Task Death01_SubmitWithoutThePlaceReportsTheMissingRequirement()
    {
        // Chuẩn bị: có đủ ngày và tệp nhưng chưa chọn nơi đăng ký
        OpenedCase setup = await OpenCaseAsync();
        DateOnly death = DeathCaseApi.Today().AddDays(-3);
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["dateOfDeath"] = DeathCaseApi.Format(death);
        fields["issuedOn"] = DeathCaseApi.Format(death.AddDays(1));
        JsonElement filled = await ApiTestHelpers.ReadJsonAsync(await PutDraftAsync(setup.Executor.Client, setup.Opened, fields));
        JsonElement withFile = await DeathCaseApi.UploadPdfAsync(setup.Executor.Client, filled.GetProperty("id").GetGuid(), "giay");

        // Hành động
        HttpResponseMessage response = await DeathCaseApi.SubmitAsync(
            setup.Executor.Client, withFile.GetProperty("id").GetGuid(), withFile.GetProperty("etag").GetString());

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_INCOMPLETE");
        List<string> missing = DeathCaseApi.ReadStrings(problem.GetProperty("missingRequirements"));
        Assert.Equal(new List<string> { "DEATH_REGISTRATION_PLACE" }, missing);
    }

    [SqlFact]
    public async Task Death01_SubmitWithAllThreeFactsAndAFileSucceeds()
    {
        // Chuẩn bị: PrepareCompleteDraftAsync tự mở hồ sơ, khai đủ 3 thông tin và tải một trang giấy
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        Assert.Empty(DeathCaseApi.ReadStrings(prepared.GetProperty("missingRequirements")));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
        Assert.Equal(DeathCaseApi.RegistrationPlace,
            submitted.GetProperty("currentVersion").GetProperty("deathRegistrationPlace").GetString());
    }

    [SqlFact]
    public async Task Death01_NewVersionCopiesThePlaceFromThePreviousVersion()
    {
        // Chuẩn bị: hồ sơ đã gửi (đang xét)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement current = await ApiTestHelpers.ReadJsonAsync(
            await submitted.Executor.Client.GetAsync(DeathCaseApi.CaseUrl(submitted.CaseId)));

        // Hành động: Executor tạo bản mới để sửa
        HttpResponseMessage response = await submitted.Executor.Client.PostAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/versions", new { etag = current.GetProperty("etag").GetString() });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement version = body.GetProperty("currentVersion");
        Assert.Equal(2, version.GetProperty("versionNo").GetInt32());
        Assert.Equal("79", version.GetProperty("deathRegistrationProvinceCode").GetString());
        Assert.Equal("26743", version.GetProperty("deathRegistrationWardCode").GetString());
    }

    [SqlFact]
    public async Task Death10_VerifierSeesThePlaceAndAWarningWhenTheOwnerCheckedInAfterTheDeathDate()
    {
        // Chuẩn bị: ngày mất = hôm nay - 3 (CreateSubmittedCaseAsync); Chủ kho điểm danh lần cuối hôm qua
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset lastCheckInAt = DateTimeOffset.UtcNow.AddDays(-1);
        await SetLastCheckInAsync(submitted.Vault.PlanId, lastCheckInAt);

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        JsonElement review = detail.GetProperty("reviewVersion");
        Assert.Equal("79", review.GetProperty("deathRegistrationProvinceCode").GetString());
        Assert.Equal("26743", review.GetProperty("deathRegistrationWardCode").GetString());
        Assert.Equal(DeathCaseApi.RegistrationPlace, review.GetProperty("deathRegistrationPlace").GetString());
        JsonElement warnings = detail.GetProperty("warnings");
        Assert.Equal(1, warnings.GetArrayLength());
        Assert.Equal("DEATH_BEFORE_LAST_CHECKIN", warnings[0].GetProperty("code").GetString());
        string expectedDay = VietnamDayText(lastCheckInAt);
        Assert.Contains(expectedDay, warnings[0].GetProperty("message").GetString());
        Assert.Equal(expectedDay, DateOnly.Parse(detail.GetProperty("lastCheckInOn").GetString()!).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        // Cảnh báo không chặn duyệt: các lý do chưa duyệt được không có mã của cảnh báo.
        foreach (string blocker in DeathCaseApi.ReadStrings(detail.GetProperty("approveBlockers")))
        {
            Assert.NotEqual("DEATH_BEFORE_LAST_CHECKIN", blocker);
        }
    }

    [SqlFact]
    public async Task Death10_VerifierGetsNoWarningWhenTheLastCheckInIsBeforeTheDeathDate()
    {
        // Chuẩn bị: ngày mất = hôm nay - 3; điểm danh cuối cách đây 10 ngày
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await SetLastCheckInAsync(submitted.Vault.PlanId, DateTimeOffset.UtcNow.AddDays(-10));

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal(0, detail.GetProperty("warnings").GetArrayLength());
    }

    [SqlFact]
    public async Task Death10_VerifierGetsNoLastCheckInWhenTheOwnerNeverCheckedIn()
    {
        // Chuẩn bị + Hành động
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("lastCheckInOn").ValueKind);
        Assert.Equal(0, detail.GetProperty("warnings").GetArrayLength());
    }

    [SqlFact]
    public async Task Death10_VerifierGetsALateRegistrationWarningButCanStillApprove()
    {
        // Chuẩn bị: đăng ký khai tử cách ngày mất 20 ngày. Ngày mất = hôm nay - 25, đăng ký = hôm nay - 5.
        (HttpClient ownerClient, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(ownerClient, vaultId, "Ghi chú thừa kế");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(ownerClient, vaultId, "Gói thừa kế"))
            .GetProperty("id").GetGuid();
        await ApiTestHelpers.AddAssetToContainerAsync(ownerClient, vaultId, containerId, assetId);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(ownerClient, vaultId, "001090000001", "Người nhận A");
        await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            ownerClient, vaultId, containerId, beneficiary.GetProperty("id").GetGuid());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await DeathCaseTestData.AddVerifierAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vaultId);

        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vaultId);
        Dictionary<string, object?> fields = new Dictionary<string, object?>();
        fields["dateOfDeath"] = DeathCaseApi.Format(DeathCaseApi.Today().AddDays(-25));
        fields["issuedOn"] = DeathCaseApi.Format(DeathCaseApi.Today().AddDays(-5));
        fields["deathRegistrationProvinceCode"] = DeathCaseApi.ProvinceCode;
        fields["deathRegistrationWardCode"] = DeathCaseApi.WardCode;
        JsonElement filled = await ApiTestHelpers.ReadJsonAsync(await PutDraftAsync(executor.Client, opened, fields));
        JsonElement withFile = await DeathCaseApi.UploadPdfAsync(executor.Client, filled.GetProperty("id").GetGuid(), "giay");
        JsonElement submittedBody = await DeathCaseApi.SubmitOkAsync(executor.Client, withFile);
        Guid caseId = submittedBody.GetProperty("id").GetGuid();
        Guid verifierId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            verifierId = await db.Set<DeathCaseAssignment>().Where(a => a.CaseId == caseId && a.EndedAt == null)
                .Select(a => a.VerifierUserId).SingleAsync();
        });
        User verifierUser = null!;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            verifierUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == verifierId);
        });
        DeathCaseReviewTestData.VerifierClient verifier = DeathCaseReviewTestData.CreateClientFor(factory, verifierUser);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, caseId, DateTimeOffset.UtcNow.AddHours(-80));

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(verifier.Client, caseId);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveAsync(
            verifier.Client, caseId, detail.GetProperty("etag").GetString(), "Giấy hợp lệ.");

        // Kiểm tra: có cảnh báo trễ, nhưng vẫn duyệt được
        JsonElement warnings = detail.GetProperty("warnings");
        Assert.Equal(1, warnings.GetArrayLength());
        Assert.Equal("LATE_REGISTRATION", warnings[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
    }

    [SqlFact]
    public async Task Death01_AfterApprovalTheRecipientDocumentHasThePlaceAndBothDates()
    {
        // Chuẩn bị: hồ sơ đã được duyệt, lượt bàn giao đầu tiên chuyển sang bước lập biên bản của một người nhận
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, "Giấy hợp lệ, thông tin khớp.");
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        User recipient = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        Guid turnId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverTurn turn = await db.Set<HandoverTurn>().SingleAsync(t => t.VaultId == submitted.Vault.VaultId);
            turn.ClaimantUserId = recipient.Id;
            turn.Status = HandoverTurnStatus.IdentityInProgress;
            await db.SaveChangesAsync();
            turnId = turn.Id;
        });
        HttpClient recipientClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(recipientClient, factory.Services, recipient);

        // Hành động
        HttpResponseMessage response = await recipientClient.PostAsJsonAsync(
            $"/api/v1/handover/turns/{turnId}/document", new { kind = "ACCEPT" });

        // Kiểm tra
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created,
            "Mã trạng thái: " + response.StatusCode);
        JsonElement document = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement owner = document.GetProperty("owner");
        DateOnly death = DeathCaseApi.Today().AddDays(-3);
        Assert.Equal(DeathCaseApi.RegistrationPlace, owner.GetProperty("deathRegistrationPlace").GetString());
        Assert.Equal(DeathCaseApi.Format(death), owner.GetProperty("dateOfDeath").GetString());
        Assert.Equal(DeathCaseApi.Format(death.AddDays(1)), owner.GetProperty("certificateIssuedOn").GetString());
    }

    [SqlFact]
    public async Task AdministrativeUnits_RequireLogin()
    {
        // Chuẩn bị + Hành động
        HttpClient anonymous = factory.CreateClient();
        HttpResponseMessage provinces = await anonymous.GetAsync("/api/v1/administrative-units/provinces");
        HttpResponseMessage wards = await anonymous.GetAsync("/api/v1/administrative-units/provinces/79/wards");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Unauthorized, provinces.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wards.StatusCode);
    }

    [SqlFact]
    public async Task AdministrativeUnits_ListProvincesAndWards()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage provincesResponse = await client.GetAsync("/api/v1/administrative-units/provinces");
        HttpResponseMessage wardsResponse = await client.GetAsync("/api/v1/administrative-units/provinces/79/wards");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, provincesResponse.StatusCode);
        JsonElement provinces = await ApiTestHelpers.ReadJsonAsync(provincesResponse);
        Assert.Equal(34, provinces.GetArrayLength());
        Assert.Equal("01", provinces[0].GetProperty("code").GetString());
        Assert.Equal("Thành phố Hà Nội", provinces[0].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.OK, wardsResponse.StatusCode);
        JsonElement wards = await ApiTestHelpers.ReadJsonAsync(wardsResponse);
        Assert.True(wards.GetArrayLength() > 0);
        bool foundBenThanh = false;
        foreach (JsonElement ward in wards.EnumerateArray())
        {
            if (ward.GetProperty("code").GetString() == "26743")
            {
                Assert.Equal("Phường Bến Thành", ward.GetProperty("name").GetString());
                foundBenThanh = true;
            }
        }
        Assert.True(foundBenThanh);
    }

    [SqlFact]
    public async Task AdministrativeUnits_WardsOfUnknownProvinceAreNotFound()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/administrative-units/provinces/99/wards");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "ADMINISTRATIVE_UNIT_NOT_FOUND");
    }

    /// <summary>Đặt lần điểm danh cuối của Chủ kho; tạo dòng lịch điểm danh nếu kế hoạch chưa có.</summary>
    private async Task SetLastCheckInAsync(Guid planId, DateTimeOffset lastCheckInAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            CheckInSchedule? schedule = await db.CheckInSchedules.SingleOrDefaultAsync(s => s.PlanId == planId);
            if (schedule == null)
            {
                schedule = new CheckInSchedule();
                schedule.PlanId = planId;
                schedule.CreatedAt = DateTimeOffset.UtcNow;
                db.CheckInSchedules.Add(schedule);
            }
            schedule.LastCheckInAt = lastCheckInAt;
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Ngày giờ Việt Nam dạng dd/MM/yyyy, giống chữ trong cảnh báo.</summary>
    private static string VietnamDayText(DateTimeOffset value)
    {
        DateOnly day = LegacyVault.Application.Services.DeathCaseRules.TodayInVietnam(value);
        return day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }
}
