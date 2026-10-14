using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Phiên bản bổ sung của hồ sơ chứng tử: tạo bản mới, xác nhận lại, báo lại Owner khi đổi trọng yếu
/// (DEATH-01, DEATH-04, DEATH-08, DEATH-11, AC-09, AC-63, D5, D8).
/// </summary>
public sealed class DeathCaseVersionFlowTests : IAsyncLifetime
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
    public async Task Ac09_Death04_EditAfterSubmitCreatesNewVersionNeedingConfirmation()
    {
        // Chuẩn bị: bản 1 đã gửi, Verifier đang xét
        Submitted submitted = await SubmitFirstVersionAsync();

        // Hành động: tạo bản bổ sung
        HttpResponseMessage response = await CreateVersionAsync(submitted.Executor.Client, submitted.Body);

        // Kiểm tra: bản 2 là bản nháp chép từ bản 1; Verifier vẫn xét bản 1
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("UNDER_REVIEW", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("reviewVersionNo").GetInt32());
        JsonElement version = body.GetProperty("currentVersion");
        Assert.Equal(2, version.GetProperty("versionNo").GetInt32());
        Assert.True(version.GetProperty("isDraft").GetBoolean());
        Assert.Equal(JsonValueKind.Null, version.GetProperty("submittedAt").ValueKind);
        Assert.Equal("Nguyễn Văn An", version.GetProperty("ownerFullName").GetString());
        Assert.Equal("8901", version.GetProperty("ownerCitizenIdLast4").GetString());
        Assert.Equal(1, version.GetProperty("files").GetArrayLength());
        Assert.Empty(DeathCaseApi.ReadStrings(body.GetProperty("missingRequirements")));

        // Phải xác nhận lại: không gửi được khi chưa confirm; gửi xong thì Verifier xét bản 2
        Guid caseId = body.GetProperty("id").GetGuid();
        HttpResponseMessage notConfirmed = await DeathCaseApi.SubmitAsync(
            submitted.Executor.Client, caseId, body.GetProperty("etag").GetString(), confirm: false);
        await ApiTestHelpers.AssertProblemAsync(notConfirmed, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        JsonElement resubmitted = await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, body);
        Assert.Equal(2, resubmitted.GetProperty("reviewVersionNo").GetInt32());
        Assert.Equal("UNDER_REVIEW", resubmitted.GetProperty("status").GetString());
        Assert.False(resubmitted.GetProperty("currentVersion").GetProperty("isDraft").GetBoolean());
        // Kế hoạch vẫn là DEATH_CASE_OPEN và trạng thái trước đó không bị ghi đè.
        DeathCase deathCase = await LoadCaseAsync(caseId);
        Assert.Equal(PlanStatus.Active, deathCase.StatusBeforeCase);
    }

    [SqlFact]
    public async Task Death04_OldVersionStaysUnchanged()
    {
        // Chuẩn bị: bản 1 đã gửi; tạo bản 2 rồi đổi họ tên, bỏ hết trang cũ và thêm trang mới
        Submitted submitted = await SubmitFirstVersionAsync();
        HttpClient client = submitted.Executor.Client;
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(await CreateVersionAsync(client, submitted.Body));
        Guid caseId = draft.GetProperty("id").GetGuid();
        DeathCaseVersion before = await LoadVersionAsync(caseId, 1);
        Guid oldFileId = draft.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage edit = await client.PutAsJsonAsync(DeathCaseApi.CaseUrl(caseId) + "/draft", new
        {
            etag = draft.GetProperty("etag").GetString(),
            ownerFullName = "Trần Văn Bình",
        });
        JsonElement edited = await ApiTestHelpers.ReadJsonAsync(edit);
        HttpResponseMessage delete = await client.DeleteAsync(DeathCaseApi.CaseUrl(caseId) + $"/files/{oldFileId}");
        await DeathCaseApi.UploadPdfAsync(client, caseId, "trang moi");

        // Kiểm tra: bản 1 nguyên vẹn (dữ liệu giải mã, ngày, file, thời điểm gửi) và nội dung dùng chung vẫn còn
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal("Trần Văn Bình", edited.GetProperty("currentVersion").GetProperty("ownerFullName").GetString());
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        DeathCaseVersion after = await LoadVersionAsync(caseId, 1);
        Assert.Equal(before.SubmittedAt, after.SubmittedAt);
        Assert.Equal(before.DateOfDeath, after.DateOfDeath);
        Assert.Equal(before.IssuedOn, after.IssuedOn);
        Assert.Equal(before.IdentityEncrypted, after.IdentityEncrypted);
        IDeathCaseDataProtector protector = factory.Services.GetRequiredService<IDeathCaseDataProtector>();
        DeathCaseIdentityData? oldData = protector.Unprotect(after);
        Assert.NotNull(oldData);
        Assert.Equal("Nguyễn Văn An", oldData.OwnerFullName);

        List<DeathCaseFile> oldFiles = await LoadFilesAsync(after.Id);
        DeathCaseFile oldFile = Assert.Single(oldFiles);
        Assert.Equal("trang.pdf", oldFile.FileName);
        // D8: bản 2 đã gỡ dòng file chép từ bản 1 nhưng nội dung vẫn còn vì bản 1 dùng chung.
        Assert.Equal(2, Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length);
    }

    [SqlFact]
    public async Task Ac63_Death11_ChangingDeathDateRenotifiesAndRestartsClock()
    {
        // Chuẩn bị: bản 1 đã gửi và đã báo Owner xong, đồng hồ 72 giờ đang chạy
        Submitted submitted = await SubmitFirstVersionAsync();
        Guid caseId = submitted.Body.GetProperty("id").GetGuid();
        await RunNoticeJobAsync();
        DeathCase afterFirst = await LoadCaseAsync(caseId);
        Assert.NotNull(afterFirst.ObjectionStartsAt);
        DateTimeOffset firstStart = afterFirst.ObjectionStartsAt.Value;

        // Hành động: bản 2 đổi ngày mất rồi gửi lại
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(await CreateVersionAsync(submitted.Executor.Client, submitted.Body));
        JsonElement edited = await EditAsync(submitted.Executor.Client, draft, new
        {
            etag = draft.GetProperty("etag").GetString(),
            dateOfDeath = DeathCaseApi.Format(DeathCaseApi.Today().AddDays(-4)),
        });
        JsonElement resubmitted = await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, edited);

        // Kiểm tra: có thông báo mới cho bản 2, đồng hồ cũ bị xóa tới khi thư mới được giao
        List<DeathCaseNotice> notices = await LoadNoticesAsync(caseId);
        Assert.Equal(2, notices.Count);
        DeathCaseNotice second = notices.Single(n => n.VersionNo == 2);
        Assert.Equal(DeathCaseNoticeStatus.Pending, second.Status);
        DeathCase renotified = await LoadCaseAsync(caseId);
        Assert.Equal(2, renotified.NotifiedVersionNo);
        Assert.Null(renotified.ObjectionStartsAt);
        Assert.Null(renotified.ObjectionEndsAt);
        Assert.Equal(JsonValueKind.Null, resubmitted.GetProperty("objectionEndsAt").ValueKind);

        // Job gửi thông báo bản 2 → đồng hồ 72 giờ chạy lại từ lúc giao thư mới
        await Task.Delay(50);
        await RunNoticeJobAsync();
        DeathCase restarted = await LoadCaseAsync(caseId);
        Assert.NotNull(restarted.ObjectionStartsAt);
        Assert.True(restarted.ObjectionStartsAt.Value > firstStart);
        Assert.Equal(restarted.ObjectionStartsAt.Value.AddHours(72), restarted.ObjectionEndsAt);
    }

    [SqlFact]
    public async Task Death11_NonCriticalChangeKeepsClock()
    {
        // Chuẩn bị: bản 1 đã gửi và đã báo Owner xong
        Submitted submitted = await SubmitFirstVersionAsync();
        Guid caseId = submitted.Body.GetProperty("id").GetGuid();
        await RunNoticeJobAsync();
        DeathCase before = await LoadCaseAsync(caseId);
        Assert.NotNull(before.ObjectionEndsAt);

        // Hành động: bản 2 chỉ đổi số giấy và nơi cấp (không trọng yếu) rồi gửi lại
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(await CreateVersionAsync(submitted.Executor.Client, submitted.Body));
        JsonElement edited = await EditAsync(submitted.Executor.Client, draft, new
        {
            etag = draft.GetProperty("etag").GetString(),
            certificateNumber = "999/2026/TT",
            issuedBy = "UBND quận 1",
        });
        JsonElement resubmitted = await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, edited);

        // Kiểm tra: không có thông báo mới, đồng hồ giữ nguyên
        Assert.Equal(2, resubmitted.GetProperty("reviewVersionNo").GetInt32());
        DeathCaseNotice notice = Assert.Single(await LoadNoticesAsync(caseId));
        Assert.Equal(1, notice.VersionNo);
        DeathCase after = await LoadCaseAsync(caseId);
        Assert.Equal(1, after.NotifiedVersionNo);
        Assert.Equal(before.ObjectionStartsAt, after.ObjectionStartsAt);
        Assert.Equal(before.ObjectionEndsAt, after.ObjectionEndsAt);
    }

    [SqlFact]
    public async Task Death01_SupplementRequiredCaseAcceptsNewVersion()
    {
        // Chuẩn bị: Verifier yêu cầu bổ sung (đặt bằng SQL; nhánh B sẽ đặt trạng thái này)
        Submitted submitted = await SubmitFirstVersionAsync();
        Guid caseId = submitted.Body.GetProperty("id").GetGuid();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == caseId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, DeathCaseStatus.SupplementRequired));
        });
        JsonElement current = await ApiTestHelpers.ReadJsonAsync(
            await submitted.Executor.Client.GetAsync(DeathCaseApi.CaseUrl(caseId)));
        Assert.Equal("SUPPLEMENT_REQUIRED", current.GetProperty("status").GetString());

        // Hành động
        HttpResponseMessage response = await CreateVersionAsync(submitted.Executor.Client, current);

        // Kiểm tra: tạo được bản 2, gửi lại thì hồ sơ về UNDER_REVIEW
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("SUPPLEMENT_REQUIRED", draft.GetProperty("status").GetString());
        JsonElement resubmitted = await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, draft);
        Assert.Equal("UNDER_REVIEW", resubmitted.GetProperty("status").GetString());
        Assert.Equal(2, resubmitted.GetProperty("reviewVersionNo").GetInt32());
    }

    [SqlFact]
    public async Task Death04_CannotCreateSecondDraftVersion()
    {
        // Chuẩn bị: bản 1 đã gửi, bản 2 đang là bản nháp
        Submitted submitted = await SubmitFirstVersionAsync();
        HttpResponseMessage first = await CreateVersionAsync(submitted.Executor.Client, submitted.Body);
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(first);

        // Hành động
        HttpResponseMessage second = await CreateVersionAsync(submitted.Executor.Client, draft);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(second, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("CASE_NOT_REVISABLE", problem.GetProperty("reason").GetString());
        Guid caseId = draft.GetProperty("id").GetGuid();
        Assert.Equal(2, (await LoadCaseAsync(caseId)).CurrentVersionNo);
    }

    [SqlFact]
    public async Task Death04_DraftCaseCannotCreateVersion()
    {
        // Chuẩn bị: hồ sơ mới mở, chưa gửi lần nào
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);

        // Hành động
        HttpResponseMessage response = await CreateVersionAsync(executor.Client, opened);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("CASE_NOT_REVISABLE", problem.GetProperty("reason").GetString());
    }

    /// <summary>Owner có kho, một Executor đã gửi bản 1 đầy đủ (Verifier chưa có nên chưa phân công).</summary>
    private async Task<Submitted> SubmitFirstVersionAsync()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement body = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        Submitted result = new Submitted();
        result.Vault = vault;
        result.Executor = executor;
        result.Body = body;
        return result;
    }

    private static Task<HttpResponseMessage> CreateVersionAsync(HttpClient client, JsonElement caseBody)
    {
        return client.PostAsJsonAsync(
            DeathCaseApi.CaseUrl(caseBody.GetProperty("id").GetGuid()) + "/versions",
            new { etag = caseBody.GetProperty("etag").GetString() });
    }

    private static async Task<JsonElement> EditAsync(HttpClient client, JsonElement caseBody, object payload)
    {
        HttpResponseMessage response = await client.PutAsJsonAsync(
            DeathCaseApi.CaseUrl(caseBody.GetProperty("id").GetGuid()) + "/draft", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Chạy job gửi mọi thông báo đang chờ (bộ gửi chỉ ghi log nên coi là đã giao).</summary>
    private async Task RunNoticeJobAsync()
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
            List<Guid> pending = await job.FindPendingNoticeIdsAsync(CancellationToken.None);
            foreach (Guid noticeId in pending)
            {
                await job.ProcessNoticeAsync(noticeId, CancellationToken.None);
            }
        }
    }

    private async Task<DeathCase> LoadCaseAsync(Guid caseId)
    {
        DeathCase? result = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == caseId);
        });
        return result!;
    }

    private async Task<DeathCaseVersion> LoadVersionAsync(Guid caseId, int versionNo)
    {
        DeathCaseVersion? result = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseVersion>().AsNoTracking()
                .SingleAsync(v => v.CaseId == caseId && v.VersionNo == versionNo);
        });
        return result!;
    }

    private async Task<List<DeathCaseFile>> LoadFilesAsync(Guid versionId)
    {
        List<DeathCaseFile> result = new List<DeathCaseFile>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseFile>().AsNoTracking().Where(f => f.VersionId == versionId).ToListAsync();
        });
        return result;
    }

    private async Task<List<DeathCaseNotice>> LoadNoticesAsync(Guid caseId)
    {
        List<DeathCaseNotice> result = new List<DeathCaseNotice>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseNotice>().AsNoTracking().Where(n => n.CaseId == caseId).ToListAsync();
        });
        return result;
    }

    private sealed class Submitted
    {
        public DeathCaseTestData.OwnerVault Vault { get; set; } = null!;

        public DeathCaseTestData.ExecutorClient Executor { get; set; } = null!;

        public JsonElement Body { get; set; }
    }
}
