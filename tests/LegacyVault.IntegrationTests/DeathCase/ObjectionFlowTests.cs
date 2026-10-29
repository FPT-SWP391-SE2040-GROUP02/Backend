using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Bộ gửi lời mời F3 giả: giữ token để test nhận lượt.</summary>
public sealed class CapturingHandoverSender : IHandoverInvitationSender
{
    public ConcurrentQueue<HandoverInvitation> Invitations { get; } = new ConcurrentQueue<HandoverInvitation>();

    public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
    {
        Invitations.Enqueue(invitation);
        return Task.CompletedTask;
    }
}

/// <summary>So khớp khuôn mặt giả: kết quả do test đặt.</summary>
public sealed class FakeOwnerFaceVerifier : IOwnerFaceVerifier
{
    public OwnerFaceOutcome Outcome { get; set; } = OwnerFaceOutcome.Unavailable;

    public Task<OwnerFaceOutcome> VerifyAsync(
        Guid ownerUserId, byte[] selfie, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(Outcome);
    }
}

/// <summary>
/// F2C Owner báo còn sống qua HTTP trên SQL Server thật (docs/api-contract.md mục 6.15, ALIVE-01..09):
/// chặn ngay, hạn không đặt lại, selfie, Verifier kết luận và hệ quả.
/// </summary>
public sealed class ObjectionFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private WebApplicationFactory<Program> app = null!;
    private readonly CapturingHandoverSender handoverSender = new CapturingHandoverSender();
    private readonly FakeOwnerFaceVerifier face = new FakeOwnerFaceVerifier();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
        app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHandoverInvitationSender>();
                services.AddSingleton<IHandoverInvitationSender>(handoverSender);
                services.RemoveAll<IOwnerFaceVerifier>();
                services.AddSingleton<IOwnerFaceVerifier>(face);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await app.DisposeAsync();
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private sealed class Scenario
    {
        public DeathCaseTestData.OwnerVault Owner { get; set; } = null!;
        public HttpClient OwnerClient { get; set; } = null!;
        public HttpClient ExecutorClient { get; set; } = null!;
        public User Verifier { get; set; } = null!;
        public HttpClient VerifierClient { get; set; } = null!;
        public Guid CaseId { get; set; }
        public int VersionNo { get; set; }
    }

    private HttpClient ClientFor(User user)
    {
        HttpClient client = app.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, app.Services, user);
        return client;
    }

    /// <summary>Hồ sơ đã gửi (UNDER_REVIEW), có gói READY để duyệt mở F3; có thể chạy thông báo và hết 72 giờ.</summary>
    private async Task<Scenario> BuildAsync(bool approvable)
    {
        Scenario scenario = new Scenario();
        scenario.Owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        Guid vaultId = scenario.Owner.VaultId;
        scenario.OwnerClient = ClientFor(scenario.Owner.Owner);

        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(scenario.OwnerClient, vaultId, "Ghi chú");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(scenario.OwnerClient, vaultId, "Gói thừa kế")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(scenario.OwnerClient, vaultId, containerId, assetId)).StatusCode);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(scenario.OwnerClient, vaultId, "001090000001", "Người nhận");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            scenario.OwnerClient, vaultId, containerId, beneficiary.GetProperty("id").GetGuid())).StatusCode);

        scenario.Verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        scenario.VerifierClient = ClientFor(scenario.Verifier);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vaultId);
        scenario.ExecutorClient = ClientFor(executor.User);

        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            scenario.ExecutorClient, vaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(scenario.ExecutorClient, prepared);
        scenario.CaseId = submitted.GetProperty("id").GetGuid();
        scenario.VersionNo = submitted.GetProperty("currentVersion").GetProperty("versionNo").GetInt32();

        if (approvable)
        {
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
                foreach (Guid id in await job.FindPendingNoticeIdsAsync(CancellationToken.None))
                {
                    await job.ProcessNoticeAsync(id, CancellationToken.None);
                }
            }
            await ApiTestHelpers.WithDbAsync(factory, async db =>
            {
                DateTimeOffset started = DateTimeOffset.UtcNow.AddHours(-80);
                DateTimeOffset ends = DateTimeOffset.UtcNow.AddHours(-8);
                await db.Set<DeathCase>().Where(c => c.Id == scenario.CaseId).ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ObjectionStartsAt, started)
                    .SetProperty(c => c.ObjectionEndsAt, ends));
            });
        }
        return scenario;
    }

    private static string OwnerUrl(Scenario s)
    {
        return $"/api/v1/vaults/{s.Owner.VaultId}/death-case";
    }

    private static string VerifierObjectionUrl(Scenario s)
    {
        return $"/api/v1/verifier/death-cases/{s.CaseId}/objection";
    }

    /// <summary>Verifier đọc etag hiện tại rồi duyệt (API duyệt dùng etag + lý do).</summary>
    private static async Task<HttpResponseMessage> ApproveAsync(Scenario s)
    {
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(s.VerifierClient, s.CaseId);
        return await DeathCaseReviewTestData.ApproveAsync(
            s.VerifierClient, s.CaseId, detail.GetProperty("etag").GetString(), "Đã đối chiếu hồ sơ.");
    }

    private static Task<HttpResponseMessage> ReportAsync(Scenario s)
    {
        return s.OwnerClient.PostAsync(OwnerUrl(s) + "/objection", null);
    }

    private static Task<HttpResponseMessage> ConcludeAsync(Scenario s, string conclusion, string reason = "Đã xem xét hồ sơ")
    {
        return s.VerifierClient.PostAsJsonAsync(VerifierObjectionUrl(s) + "/conclude", new { conclusion, reason });
    }

    private static Task<HttpResponseMessage> SelfieAsync(Scenario s)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        ByteArrayContent image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 });
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", "selfie.jpg");
        return s.OwnerClient.PostAsync(OwnerUrl(s) + "/objection/selfie", form);
    }

    private async Task<(DeathCaseStatus Case, PlanStatus Plan)> ReadStatusAsync(Scenario s)
    {
        DeathCaseStatus caseStatus = DeathCaseStatus.Draft;
        PlanStatus planStatus = PlanStatus.Draft;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            caseStatus = await db.Set<DeathCase>().Where(c => c.Id == s.CaseId).Select(c => c.Status).SingleAsync();
            planStatus = await db.HandoverPlans.Where(p => p.VaultId == s.Owner.VaultId).Select(p => p.Status).SingleAsync();
        });
        return (caseStatus, planStatus);
    }

    [SqlFact]
    public async Task Alive01_Report_BlocksImmediately_RepeatDoesNotResetDeadline()
    {
        Scenario s = await BuildAsync(approvable: true);

        HttpResponseMessage first = await ReportAsync(s);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);
        JsonElement objection = firstBody.GetProperty("objection");
        Assert.Equal("SUSPENDED", firstBody.GetProperty("status").GetString());
        Assert.Equal("OPEN", objection.GetProperty("status").GetString());
        Assert.Equal(1, objection.GetProperty("reportCount").GetInt32());
        DateTimeOffset deadline = objection.GetProperty("selfieDeadlineAt").GetDateTimeOffset();

        (DeathCaseStatus caseStatus, PlanStatus planStatus) = await ReadStatusAsync(s);
        Assert.Equal(DeathCaseStatus.Suspended, caseStatus);
        Assert.Equal(PlanStatus.Blocked, planStatus);

        // Duyệt bị chặn dù đã đủ 72 giờ
        HttpResponseMessage approve = await ApproveAsync(s);
        JsonElement blocked = await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("NOT_UNDER_REVIEW", blocked.GetProperty("reason").GetString());

        // Owner không sửa được kho khi bị chặn
        HttpResponseMessage edit = await s.OwnerClient.PostAsJsonAsync(
            $"/api/v1/vaults/{s.Owner.VaultId}/containers", new { name = "Gói mới" });
        await ApiTestHelpers.AssertProblemAsync(edit, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");

        // Báo lặp: chỉ tăng bộ đếm, hạn giữ nguyên (ALIVE-09)
        JsonElement repeat = (await ApiTestHelpers.ReadJsonAsync(await ReportAsync(s))).GetProperty("objection");
        Assert.Equal(2, repeat.GetProperty("reportCount").GetInt32());
        Assert.Equal(deadline, repeat.GetProperty("selfieDeadlineAt").GetDateTimeOffset());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseObjection>().CountAsync(o => o.CaseId == s.CaseId));
        });

        // Hồ sơ vào hàng chờ SUSPENDED của Verifier
        JsonElement queue = await ApiTestHelpers.ReadJsonAsync(await s.VerifierClient.GetAsync("/api/v1/verifier/death-cases"));
        Assert.Equal("SUSPENDED", queue.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Alive01_TwoFirstReportsAtOnce_OneObjection()
    {
        Scenario s = await BuildAsync(approvable: false);

        HttpResponseMessage[] responses = await Task.WhenAll(ReportAsync(s), ReportAsync(s));

        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict,
                await response.Content.ReadAsStringAsync());
        }
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseObjection>().CountAsync(o => o.CaseId == s.CaseId));
        });
    }

    [SqlFact]
    public async Task Alive01_OnlyVaultOwnerCanReport_DraftCaseHasNothingToReport()
    {
        Scenario s = await BuildAsync(approvable: false);
        DeathCaseTestData.OwnerVault other = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        HttpClient otherClient = ClientFor(other.Owner);

        HttpResponseMessage stranger = await otherClient.PostAsync(OwnerUrl(s) + "/objection", null);
        HttpResponseMessage noCase = await otherClient.PostAsync($"/api/v1/vaults/{other.VaultId}/death-case/objection", null);
        HttpResponseMessage executor = await s.ExecutorClient.PostAsync(OwnerUrl(s) + "/objection", null);

        await ApiTestHelpers.AssertProblemAsync(stranger, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(noCase, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(executor, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task Alive02_Selfie_UnavailableIs503_NotMatchedLimited_MatchedPasses()
    {
        Scenario s = await BuildAsync(approvable: false);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);

        // Chưa có mẫu khuôn mặt: lỗi kỹ thuật, không tính thất bại
        face.Outcome = OwnerFaceOutcome.Unavailable;
        await ApiTestHelpers.AssertProblemAsync(await SelfieAsync(s), HttpStatusCode.ServiceUnavailable, "EKYC_SERVICE_UNAVAILABLE");
        JsonElement afterTech = (await ApiTestHelpers.ReadJsonAsync(await s.OwnerClient.GetAsync(OwnerUrl(s)))).GetProperty("objection");
        Assert.Equal(3, afterTech.GetProperty("selfieAttemptsRemaining").GetInt32());

        // Không khớp ba lần thì khóa
        face.Outcome = OwnerFaceOutcome.NotMatched;
        for (int i = 0; i < 3; i++)
        {
            HttpResponseMessage attempt = await SelfieAsync(s);
            Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
            Assert.False((await ApiTestHelpers.ReadJsonAsync(attempt)).GetProperty("objection").GetProperty("selfiePassed").GetBoolean());
        }
        JsonElement locked = await ApiTestHelpers.AssertProblemAsync(await SelfieAsync(s), HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("SELFIE_ATTEMPTS_EXCEEDED", locked.GetProperty("reason").GetString());

        // Hết cửa sổ 24 giờ thì thử lại được và khớp
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.SelfieFailureWindowStartedAt, DateTimeOffset.UtcNow.AddHours(-25)));
        });
        face.Outcome = OwnerFaceOutcome.Matched;
        JsonElement passed = (await ApiTestHelpers.ReadJsonAsync(await SelfieAsync(s))).GetProperty("objection");
        Assert.True(passed.GetProperty("selfiePassed").GetBoolean());
    }

    [SqlFact]
    public async Task Alive03_SelfieAfterSevenDays_Rejected()
    {
        Scenario s = await BuildAsync(approvable: false);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.SelfieDeadlineAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        });
        face.Outcome = OwnerFaceOutcome.Matched;

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(await SelfieAsync(s), HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");

        Assert.Equal("OBJECTION_WINDOW_CLOSED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Alive05_AliveConfirmed_NeedsEvidence_ThenRejectsCaseAndResetsPlan()
    {
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);

        // Chưa có selfie đạt, chưa hết 7 ngày: chưa kết luận "còn sống" được
        JsonElement noEvidence = await ApiTestHelpers.AssertProblemAsync(
            await ConcludeAsync(s, "ALIVE_CONFIRMED"), HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("ALIVE_EVIDENCE_REQUIRED", noEvidence.GetProperty("reason").GetString());

        face.Outcome = OwnerFaceOutcome.Matched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);
        HttpResponseMessage concluded = await ConcludeAsync(s, "ALIVE_CONFIRMED");

        Assert.Equal(HttpStatusCode.OK, concluded.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(concluded);
        Assert.Equal("CONCLUDED", body.GetProperty("status").GetString());
        Assert.Equal("ALIVE_CONFIRMED", body.GetProperty("conclusion").GetString());
        (DeathCaseStatus caseStatus, PlanStatus planStatus) = await ReadStatusAsync(s);
        Assert.Equal(DeathCaseStatus.Rejected, caseStatus);
        Assert.Equal(PlanStatus.Active, planStatus);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.CheckInPeriods.CountAsync(p => p.Plan.VaultId == s.Owner.VaultId && p.ClosedAt == null));

        });
        // Phân công kết thúc cùng hồ sơ: Verifier không còn truy cập được hồ sơ đã đóng
        Assert.Equal(HttpStatusCode.NotFound, (await ConcludeAsync(s, "ALIVE_CONFIRMED")).StatusCode);

        // Owner điểm danh lại bình thường và Executor mở được hồ sơ mới
        Assert.Equal(HttpStatusCode.OK, (await s.OwnerClient.GetAsync("/api/v1/check-ins/me")).StatusCode);
        HttpResponseMessage reopen = await s.ExecutorClient.PostAsync($"/api/v1/executor/vaults/{s.Owner.VaultId}/death-case", null);
        Assert.Equal(HttpStatusCode.Created, reopen.StatusCode);
    }

    [SqlFact]
    public async Task Alive05_ObjectionRejected_RestoresCase_DoesNotApprove()
    {
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);

        HttpResponseMessage insufficient = await ConcludeAsync(s, "INSUFFICIENT_EVIDENCE", "Cần thêm căn cứ");
        Assert.Equal(HttpStatusCode.OK, insufficient.StatusCode);
        JsonElement still = await ApiTestHelpers.ReadJsonAsync(insufficient);
        Assert.Equal("OPEN", still.GetProperty("status").GetString());
        Assert.Equal(1, still.GetProperty("insufficientEvidenceCount").GetInt32());
        Assert.Equal(DeathCaseStatus.Suspended, (await ReadStatusAsync(s)).Case);

        HttpResponseMessage rejected = await ConcludeAsync(s, "OBJECTION_REJECTED", "Không có căn cứ");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        (DeathCaseStatus caseStatus, PlanStatus planStatus) = await ReadStatusAsync(s);
        Assert.Equal(DeathCaseStatus.UnderReview, caseStatus);
        Assert.Equal(PlanStatus.DeathCaseOpen, planStatus);

        // Báo lại không có bằng chứng mới (ALIVE-09)
        JsonElement again = await ApiTestHelpers.AssertProblemAsync(await ReportAsync(s), HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("OBJECTION_ALREADY_REJECTED", again.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Alive05_SelfiePassed_RejectIsNotAllowed_CaseStaysBlocked()
    {
        // Chuẩn bị: Owner báo còn sống và selfie khớp
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        face.Outcome = OwnerFaceOutcome.Matched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);

        // Hành động: Verifier cố bác phản đối
        HttpResponseMessage rejected = await ConcludeAsync(s, "OBJECTION_REJECTED", "Không có căn cứ");

        // Kiểm tra: bị chặn, không ghi gì; lời báo vẫn mở, hồ sơ vẫn SUSPENDED, hold còn
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(rejected, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("OWNER_SELFIE_PASSED", problem.GetProperty("reason").GetString());
        JsonElement objection = await ApiTestHelpers.ReadJsonAsync(await s.VerifierClient.GetAsync(VerifierObjectionUrl(s)));
        Assert.Equal("OPEN", objection.GetProperty("status").GetString());
        (DeathCaseStatus caseStatus, PlanStatus planStatus) = await ReadStatusAsync(s);
        Assert.Equal(DeathCaseStatus.Suspended, caseStatus);
        Assert.Equal(PlanStatus.Blocked, planStatus);
        JsonElement blocked = await ApiTestHelpers.AssertProblemAsync(
            await ApproveAsync(s), HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("NOT_UNDER_REVIEW", blocked.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseObjection>().CountAsync(
                o => o.CaseId == s.CaseId && o.Status == ObjectionStatus.Open));
        });
    }

    [SqlFact]
    public async Task Alive06_InsufficientEvidence_AsksOwnerForNewSelfie_ThenAliveAllowedAgain()
    {
        // Chuẩn bị: lời báo đã có selfie khớp
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        face.Outcome = OwnerFaceOutcome.Matched;
        JsonElement firstPass = (await ApiTestHelpers.ReadJsonAsync(await SelfieAsync(s))).GetProperty("objection");
        DateTimeOffset firstDeadline = firstPass.GetProperty("selfieDeadlineAt").GetDateTimeOffset();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            // Lần thử đầu đã dùng 1 lượt không khớp: yêu cầu chụp lại phải trả đủ lượt
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId).ExecuteUpdateAsync(x => x
                .SetProperty(o => o.SelfieFailedCount, 1)
                .SetProperty(o => o.SelfieFailureWindowStartedAt, DateTimeOffset.UtcNow));
        });

        // Hành động: Verifier ghi chưa đủ bằng chứng
        HttpResponseMessage insufficient = await ConcludeAsync(s, "INSUFFICIENT_EVIDENCE", "Ảnh mờ, cần chụp lại");

        // Kiểm tra: vẫn chặn, nhưng Owner được yêu cầu chụp selfie mới trong 7 ngày mới, đủ 3 lượt
        Assert.Equal(HttpStatusCode.OK, insufficient.StatusCode);
        JsonElement asked = await ApiTestHelpers.ReadJsonAsync(insufficient);
        Assert.Equal("OPEN", asked.GetProperty("status").GetString());
        Assert.Equal(1, asked.GetProperty("insufficientEvidenceCount").GetInt32());
        Assert.Equal("Ảnh mờ, cần chụp lại", asked.GetProperty("conclusionReason").GetString());
        Assert.False(asked.GetProperty("selfiePassed").GetBoolean());
        Assert.True(asked.GetProperty("selfieWindowOpen").GetBoolean());
        Assert.Equal(3, asked.GetProperty("selfieAttemptsRemaining").GetInt32());
        DateTimeOffset newDeadline = asked.GetProperty("selfieDeadlineAt").GetDateTimeOffset();
        Assert.True(newDeadline > firstDeadline);
        Assert.True(newDeadline > DateTimeOffset.UtcNow.AddDays(6));
        Assert.True(asked.GetProperty("verifierDueAt").GetDateTimeOffset() >= newDeadline.AddHours(48));
        Assert.Equal(DeathCaseStatus.Suspended, (await ReadStatusAsync(s)).Case);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Hold hold = await db.Set<Hold>().SingleAsync(h => h.VaultId == s.Owner.VaultId && h.Status == HoldStatus.Active);
            Assert.Equal(newDeadline, hold.ReviewAt);
        });

        // Owner thấy yêu cầu và lý do
        JsonElement ownerView = (await ApiTestHelpers.ReadJsonAsync(await s.OwnerClient.GetAsync(OwnerUrl(s)))).GetProperty("objection");
        Assert.False(ownerView.GetProperty("selfiePassed").GetBoolean());
        Assert.Equal("Ảnh mờ, cần chụp lại", ownerView.GetProperty("conclusionReason").GetString());

        // Chưa có selfie mới thì chưa xác nhận còn sống được bằng selfie cũ
        JsonElement noEvidence = await ApiTestHelpers.AssertProblemAsync(
            await ConcludeAsync(s, "ALIVE_CONFIRMED", "Selfie cũ"), HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("ALIVE_EVIDENCE_REQUIRED", noEvidence.GetProperty("reason").GetString());

        // Owner chụp selfie mới khớp thì Verifier xác nhận còn sống được
        JsonElement secondPass = (await ApiTestHelpers.ReadJsonAsync(await SelfieAsync(s))).GetProperty("objection");
        Assert.True(secondPass.GetProperty("selfiePassed").GetBoolean());
        HttpResponseMessage alive = await ConcludeAsync(s, "ALIVE_CONFIRMED", "Selfie mới đã khớp");
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        Assert.Equal("ALIVE_CONFIRMED", (await ApiTestHelpers.ReadJsonAsync(alive)).GetProperty("conclusion").GetString());
        Assert.Equal(DeathCaseStatus.Rejected, (await ReadStatusAsync(s)).Case);
    }

    [SqlFact]
    public async Task Alive06_InsufficientEvidence_OwnerNeverRetakes_RejectAllowedAfterNewWindow()
    {
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        face.Outcome = OwnerFaceOutcome.Matched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConcludeAsync(s, "INSUFFICIENT_EVIDENCE", "Cần chụp lại")).StatusCode);

        // Owner không chụp lại, hết 7 ngày mới
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.SelfieDeadlineAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });

        HttpResponseMessage rejected = await ConcludeAsync(s, "OBJECTION_REJECTED", "Owner không chụp lại");

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(DeathCaseStatus.UnderReview, (await ReadStatusAsync(s)).Case);
    }

    [SqlFact]
    public async Task Alive05_NoSelfieAfterWindow_RejectStillAllowed()
    {
        // Chuẩn bị: báo còn sống, không selfie, quá 7 ngày
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.SelfieDeadlineAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        });

        // Hành động
        HttpResponseMessage rejected = await ConcludeAsync(s, "OBJECTION_REJECTED", "Không có selfie");

        // Kiểm tra: bác như cũ, hồ sơ trở về UNDER_REVIEW
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("OBJECTION_REJECTED", (await ApiTestHelpers.ReadJsonAsync(rejected)).GetProperty("conclusion").GetString());
        Assert.Equal(DeathCaseStatus.UnderReview, (await ReadStatusAsync(s)).Case);
    }

    [SqlFact]
    public async Task Alive09_ReopenedWithMatchedSelfie_CannotBeRejectedAgain()
    {
        // Chuẩn bị: báo, không selfie, quá 7 ngày, Verifier bác lần 1
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.SelfieDeadlineAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        });
        Assert.Equal(HttpStatusCode.OK, (await ConcludeAsync(s, "OBJECTION_REJECTED", "Lần 1")).StatusCode);

        // Hành động: Owner gửi selfie khớp để mở lại, Verifier bác lần 2
        face.Outcome = OwnerFaceOutcome.Matched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);
        Assert.Equal(DeathCaseStatus.Suspended, (await ReadStatusAsync(s)).Case);
        HttpResponseMessage second = await ConcludeAsync(s, "OBJECTION_REJECTED", "Lần 2");

        // Kiểm tra: bị chặn, lời báo mở lại vẫn mở
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(second, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("OWNER_SELFIE_PASSED", problem.GetProperty("reason").GetString());
        Assert.Equal(DeathCaseStatus.Suspended, (await ReadStatusAsync(s)).Case);
    }

    [SqlFact]
    public async Task Alive07_GrantBlockedThenRevoked_ClaimBlockedWhileHeld()
    {
        Scenario s = await BuildAsync(approvable: true);
        HttpResponseMessage approve = await ApproveAsync(s);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.True(handoverSender.Invitations.TryPeek(out HandoverInvitation? invitation));
        string token = invitation!.Token;

        // Người nhận nhận lượt và đồng ý: có grant
        User recipient = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient recipientClient = ClientFor(recipient);
        HttpResponseMessage claim = await recipientClient.PostAsync($"/api/v1/handover/invitations/{token}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Guid turnId = (await ApiTestHelpers.ReadJsonAsync(claim)).GetProperty("id").GetGuid();
        HttpResponseMessage accept = await recipientClient.PostAsync($"/api/v1/handover/turns/{turnId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Guid grantId = (await ApiTestHelpers.ReadJsonAsync(accept)).GetProperty("grant").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await recipientClient.GetAsync($"/api/v1/handover/grants/{grantId}/assets")).StatusCode);

        // Owner báo còn sống: grant bị chặn ngay
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        JsonElement denied = await ApiTestHelpers.AssertProblemAsync(
            await recipientClient.GetAsync($"/api/v1/handover/grants/{grantId}/assets"), HttpStatusCode.Forbidden, "GRANT_NOT_ACTIVE");
        Assert.Equal("BLOCKED", denied.GetProperty("reason").GetString());

        // Bác phản đối: grant dùng lại được
        Assert.Equal(HttpStatusCode.OK, (await ConcludeAsync(s, "OBJECTION_REJECTED")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await recipientClient.GetAsync($"/api/v1/handover/grants/{grantId}/assets")).StatusCode);
    }

    [SqlFact]
    public async Task Alive07_AliveConfirmed_CancelsTurnsAndRevokesGrants()
    {
        Scenario s = await BuildAsync(approvable: true);
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s)).StatusCode);
        Assert.True(handoverSender.Invitations.TryPeek(out HandoverInvitation? invitation));
        string token = invitation!.Token;

        // Owner báo còn sống trước khi người nhận kịp nhận: không nhận lượt được
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        User recipient = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpResponseMessage claim = await ClientFor(recipient).PostAsync($"/api/v1/handover/invitations/{token}/claim", null);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(claim, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("PLAN_NOT_IN_HANDOVER", problem.GetProperty("reason").GetString());

        face.Outcome = OwnerFaceOutcome.Matched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConcludeAsync(s, "ALIVE_CONFIRMED")).StatusCode);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverTurn> turns = await db.Set<HandoverTurn>().Where(t => t.VaultId == s.Owner.VaultId).ToListAsync();
            Assert.NotEmpty(turns);
            Assert.All(turns, t => Assert.Equal(HandoverTurnStatus.Cancelled, t.Status));
        });
    }

    [SqlFact]
    public async Task OtherVerifier_CannotSeeOrConclude()
    {
        Scenario s = await BuildAsync(approvable: false);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);
        User other = await DeathCaseTestData.AddVerifierAsync(factory);
        HttpClient otherClient = ClientFor(other);

        HttpResponseMessage get = await otherClient.GetAsync(VerifierObjectionUrl(s));
        HttpResponseMessage conclude = await otherClient.PostAsJsonAsync(
            VerifierObjectionUrl(s) + "/conclude", new { conclusion = "OBJECTION_REJECTED", reason = "x" });
        HttpResponseMessage invalid = await ConcludeAsync(s, "KHONG_HOP_LE");

        await ApiTestHelpers.AssertProblemAsync(get, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(conclude, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Byte ảnh selfie mà SelfieAsync gửi (có chữ ký JPEG nên được lưu).</summary>
    private static readonly byte[] SelfieBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };

    [SqlFact]
    public async Task Br50_VerifierSeesLatestSelfieImage_NewAttemptReplacesOld()
    {
        Scenario s = await BuildAsync(approvable: false);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(s)).StatusCode);

        // Chưa có lần thử nào: chưa có ảnh
        JsonElement before = await ApiTestHelpers.ReadJsonAsync(await s.VerifierClient.GetAsync(VerifierObjectionUrl(s)));
        Assert.False(before.GetProperty("hasSelfieImage").GetBoolean());
        await ApiTestHelpers.AssertProblemAsync(
            await s.VerifierClient.GetAsync(VerifierObjectionUrl(s) + "/selfie-image"),
            HttpStatusCode.NotFound, "IDENTITY_IMAGE_NOT_FOUND");

        // Lỗi kỹ thuật không lưu ảnh
        face.Outcome = OwnerFaceOutcome.Unavailable;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SelfieAsync(s)).StatusCode);
        Assert.False((await ApiTestHelpers.ReadJsonAsync(await s.VerifierClient.GetAsync(VerifierObjectionUrl(s))))
            .GetProperty("hasSelfieImage").GetBoolean());

        // Lần thử không khớp vẫn được lưu để Verifier xem
        face.Outcome = OwnerFaceOutcome.NotMatched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);
        JsonElement failed = await ApiTestHelpers.ReadJsonAsync(await s.VerifierClient.GetAsync(VerifierObjectionUrl(s)));
        Assert.True(failed.GetProperty("hasSelfieImage").GetBoolean());
        Assert.False(failed.GetProperty("selfieImageMatched").GetBoolean());
        string? firstKey = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            firstKey = await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .Select(o => o.SelfieImageStorageKey).SingleAsync();
        });
        Assert.NotNull(firstKey);

        HttpResponseMessage image = await s.VerifierClient.GetAsync(VerifierObjectionUrl(s) + "/selfie-image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SelfieBytes, await image.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", image.Headers.CacheControl?.ToString());

        // Lần thử khớp thay ảnh cũ
        face.Outcome = OwnerFaceOutcome.Matched;
        Assert.Equal(HttpStatusCode.OK, (await SelfieAsync(s)).StatusCode);
        JsonElement passed = await ApiTestHelpers.ReadJsonAsync(await s.VerifierClient.GetAsync(VerifierObjectionUrl(s)));
        Assert.True(passed.GetProperty("selfieImageMatched").GetBoolean());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            string? latestKey = await db.Set<DeathCaseObjection>().Where(o => o.CaseId == s.CaseId)
                .Select(o => o.SelfieImageStorageKey).SingleAsync();
            Assert.NotNull(latestKey);
            Assert.NotEqual(firstKey, latestKey);
        });

        // Verifier khác không xem được
        User other = await DeathCaseTestData.AddVerifierAsync(factory);
        await ApiTestHelpers.AssertProblemAsync(
            await ClientFor(other).GetAsync(VerifierObjectionUrl(s) + "/selfie-image"),
            HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    [SqlFact]
    public async Task Br48_VerifierSeesVerifiedOwnerIdentity_AndDocumentImage()
    {
        Scenario s = await BuildAsync(approvable: false);
        string detailUrl = $"/api/v1/verifier/death-cases/{s.CaseId}";

        // Owner chưa xác minh: không có danh tính, không có ảnh
        JsonElement unverified = (await DeathCaseReviewTestData.GetDetailAsync(s.VerifierClient, s.CaseId)).GetProperty("ownerIdentity");
        Assert.False(unverified.GetProperty("verified").GetBoolean());
        Assert.False(unverified.GetProperty("hasDocumentImage").GetBoolean());
        await ApiTestHelpers.AssertProblemAsync(
            await s.VerifierClient.GetAsync(detailUrl + "/owner-identity/document-image"),
            HttpStatusCode.NotFound, "IDENTITY_IMAGE_NOT_FOUND");

        // Owner đã xác minh: cùng họ tên (khác hoa thường), khác CCCD với bản Executor khai
        byte[] documentBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9, 9 };
        using (IServiceScope scope = app.Services.CreateScope())
        {
            IAssetContentStore store = scope.ServiceProvider.GetRequiredService<IAssetContentStore>();
            IOwnerIdentityProtector protector = scope.ServiceProvider.GetRequiredService<IOwnerIdentityProtector>();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            StoredContent stored = await IdentityImageStorage.SaveAsync(store, documentBytes, CancellationToken.None);

            OwnerIdentity identity = new OwnerIdentity();
            identity.UserId = s.Owner.Owner.Id;
            identity.SourceVerificationId = Guid.NewGuid();
            identity.CreatedAt = DateTimeOffset.UtcNow;
            identity.DocumentImageStorageKey = stored.StorageKey;
            identity.DocumentImageContentType = "image/png";
            identity.DocumentImageSizeBytes = stored.SizeBytes;
            identity.DocumentImageEncryptionKeyId = stored.EncryptionKeyId;
            identity.DocumentImageWrappedDataKey = stored.WrappedDataKey;
            identity.DocumentImageCapturedAt = DateTimeOffset.UtcNow;
            OwnerIdentityData data = new OwnerIdentityData();
            data.CitizenId = "012345678900";
            data.FullName = "NGUYỄN VĂN AN";
            data.DateOfBirth = new DateOnly(1950, 3, 4);
            protector.Protect(identity, data);
            db.Add(identity);
            await db.SaveChangesAsync();
        }

        JsonElement verified = (await DeathCaseReviewTestData.GetDetailAsync(s.VerifierClient, s.CaseId)).GetProperty("ownerIdentity");
        Assert.True(verified.GetProperty("verified").GetBoolean());
        Assert.Equal("012345678900", verified.GetProperty("citizenId").GetString());
        Assert.Equal("NGUYỄN VĂN AN", verified.GetProperty("fullName").GetString());
        Assert.Equal("1950-03-04", verified.GetProperty("dateOfBirth").GetString());
        Assert.False(verified.GetProperty("citizenIdMatches").GetBoolean());
        Assert.True(verified.GetProperty("fullNameMatches").GetBoolean());
        Assert.True(verified.GetProperty("hasDocumentImage").GetBoolean());

        HttpResponseMessage image = await s.VerifierClient.GetAsync(detailUrl + "/owner-identity/document-image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(documentBytes, await image.Content.ReadAsByteArrayAsync());

        // Verifier không được giao và Owner đều không xem được ảnh
        User other = await DeathCaseTestData.AddVerifierAsync(factory);
        await ApiTestHelpers.AssertProblemAsync(
            await ClientFor(other).GetAsync(detailUrl + "/owner-identity/document-image"),
            HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        Assert.NotEqual(HttpStatusCode.OK, (await s.OwnerClient.GetAsync(detailUrl + "/owner-identity/document-image")).StatusCode);
    }
}
