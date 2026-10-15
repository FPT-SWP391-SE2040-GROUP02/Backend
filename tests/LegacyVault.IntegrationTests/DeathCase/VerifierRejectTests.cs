using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Verifier từ chối hồ sơ chứng tử: đóng hồ sơ, trả kế hoạch về trạng thái trước, hủy thông báo Owner
/// (DEATH-05, DEATH-08, DEATH-12, DMS-02, DMS-04, AC-71, AC-107).
/// </summary>
public sealed class VerifierRejectTests : IAsyncLifetime
{
    private const string RejectReason = "Giấy chứng tử không hợp lệ.";

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
    public async Task Death12_RejectRestoresStatusBeforeCase()
    {
        // Chuẩn bị: Owner đã nhận thư (thông báo Delivered); kế hoạch bị khóa DEATH_CASE_OPEN lúc gửi
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset deliveredAt = DateTimeOffset.UtcNow.AddHours(-10);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, submitted.CaseId, deliveredAt);
        HandoverPlan planBefore = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.DeathCaseOpen, planBefore.Status);
        DeathCaseAssignment? assignmentBefore = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(assignmentBefore);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, "  " + RejectReason + "  ");
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra: hồ sơ đóng, kế hoạch về ACTIVE (StatusBeforeCase), phân công và thông báo Delivered giữ nguyên
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("REJECTED", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("closedAt").ValueKind);
        Assert.False(body.GetProperty("canApprove").GetBoolean());
        JsonElement decision = body.GetProperty("decisions")[0];
        Assert.Equal("REJECTED", decision.GetProperty("kind").GetString());
        Assert.Equal(1, decision.GetProperty("versionNo").GetInt32());
        Assert.Equal(RejectReason, decision.GetProperty("reason").GetString());
        Assert.Equal("DELIVERED", body.GetProperty("ownerNotice").GetProperty("status").GetString());

        HandoverPlan planAfter = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.Active, planAfter.Status);
        DeathCaseAssignment? assignmentAfter = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(assignmentAfter);
        Assert.Equal(assignmentBefore.Id, assignmentAfter.Id);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.Rejected, stored.Status);
            Assert.NotNull(stored.ClosedAt);
            Assert.Equal(PlanStatus.Active, stored.StatusBeforeCase);
            Assert.Equal(deliveredAt.AddHours(72), stored.ObjectionEndsAt);
            DeathCaseNotice notice = await db.Set<DeathCaseNotice>().AsNoTracking().SingleAsync(n => n.CaseId == submitted.CaseId);
            Assert.Equal(DeathCaseNoticeStatus.Delivered, notice.Status);
            DeathCaseDecision stored1 = await db.Set<DeathCaseDecision>().AsNoTracking().SingleAsync(d => d.CaseId == submitted.CaseId);
            Assert.Equal(submitted.AssignmentId, stored1.AssignmentId);
            Assert.Equal(submitted.Verifier.User.Id, stored1.VerifierUserId);
        });
    }

    [SqlFact]
    public async Task Ac107_RejectWhenDueAtPassedThenJobMovesToCheckinOverdue()
    {
        // Chuẩn bị: kỳ điểm danh đã quá hạn trong lúc hồ sơ đang xét (kế hoạch đang bị khóa nên job không đụng tới)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid periodId = await DeathCaseReviewTestData.AddOpenPeriodAsync(factory, submitted.Vault.PlanId, 31);

        // Hành động: từ chối (kế hoạch về ACTIVE), rồi job tính lại theo kỳ
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HandoverPlan afterReject = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        await RunCheckInJobAsync(periodId);

        // Kiểm tra
        Assert.Equal(PlanStatus.Active, afterReject.Status);
        HandoverPlan afterJob = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.CheckinOverdue, afterJob.Status);
        CheckInPeriod period = await LoadPeriodAsync(periodId);
        Assert.NotNull(period.OverdueAt);
        Assert.Null(period.AwaitingCheckAt);
    }

    [SqlFact]
    public async Task Ac107_RejectFromCheckinOverdueThenJobMovesToAwaitingCheck()
    {
        // Chuẩn bị: gửi lúc CHECKIN_OVERDUE; trong lúc xét, thời gian chờ cũng đã hết
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetStatusBeforeCaseAsync(factory, submitted.CaseId, PlanStatus.CheckinOverdue);
        Guid periodId = await DeathCaseReviewTestData.AddOpenPeriodAsync(factory, submitted.Vault.PlanId, 40);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.CheckInPeriods.Where(p => p.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.OverdueAt, DateTimeOffset.UtcNow.AddDays(-10)));
        });

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HandoverPlan afterReject = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        await RunCheckInJobAsync(periodId);

        // Kiểm tra: AwaitingCheckAt được ghi lần đầu
        Assert.Equal(PlanStatus.CheckinOverdue, afterReject.Status);
        HandoverPlan afterJob = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.AwaitingCheck, afterJob.Status);
        CheckInPeriod period = await LoadPeriodAsync(periodId);
        Assert.NotNull(period.AwaitingCheckAt);
    }

    [SqlFact]
    public async Task Ac107_RejectFromAwaitingCheckKeepsAwaitingCheckAt()
    {
        // Chuẩn bị: gửi lúc AWAITING_CHECK, kỳ đã ghi AwaitingCheckAt
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetStatusBeforeCaseAsync(factory, submitted.CaseId, PlanStatus.AwaitingCheck);
        Guid periodId = await DeathCaseReviewTestData.AddOpenPeriodAsync(factory, submitted.Vault.PlanId, 40);
        DateTimeOffset awaitingCheckAt = new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.CheckInPeriods.Where(p => p.Id == periodId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.OverdueAt, awaitingCheckAt.AddDays(-7))
                    .SetProperty(p => p.AwaitingCheckAt, awaitingCheckAt));
        });

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await RunCheckInJobAsync(periodId);

        // Kiểm tra (DMS-02): job không đổi gì, AwaitingCheckAt không bị ghi lại
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.AwaitingCheck, plan.Status);
        CheckInPeriod period = await LoadPeriodAsync(periodId);
        Assert.Equal(awaitingCheckAt, period.AwaitingCheckAt);
    }

    [SqlFact]
    public async Task Death05_ExecutorOpensNewCaseAfterRejectWithPreviousCase()
    {
        // Chuẩn bị: hồ sơ đầu bị từ chối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        // Hành động: Executor mở hồ sơ mới của cùng kho
        HttpResponseMessage opened = await submitted.Executor.Client.PostAsync(DeathCaseApi.OpenUrl(submitted.Vault.VaultId), null);
        JsonElement newCase = await ApiTestHelpers.ReadJsonAsync(opened);

        // Kiểm tra: hồ sơ mới ở DRAFT, nối PreviousCaseId tới hồ sơ bị từ chối; hồ sơ cũ vẫn REJECTED
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        Guid newCaseId = newCase.GetProperty("id").GetGuid();
        Assert.NotEqual(submitted.CaseId, newCaseId);
        Assert.Equal("DRAFT", newCase.GetProperty("status").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == newCaseId);
            Assert.Equal(submitted.CaseId, stored.PreviousCaseId);
        });
        JsonElement oldCase = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);
        Assert.Equal("REJECTED", oldCase.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Death08_RejectCancelsPendingNoticeAndJobDoesNotSend()
    {
        // Chuẩn bị: thông báo Owner còn Pending (job chưa gửi)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid noticeId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseNotice notice = await db.Set<DeathCaseNotice>().AsNoTracking().SingleAsync(n => n.CaseId == submitted.CaseId);
            Assert.Equal(DeathCaseNoticeStatus.Pending, notice.Status);
            noticeId = notice.Id;
        });

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        DeathCaseNoticeStatus? jobResult = null;
        List<Guid> pendingIds = new List<Guid>();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
            pendingIds = await job.FindPendingNoticeIdsAsync(CancellationToken.None);
            jobResult = await job.ProcessNoticeAsync(noticeId, CancellationToken.None);
        }

        // Kiểm tra: thông báo CANCELLED, job bỏ qua, đồng hồ 72 giờ không chạy
        Assert.Equal("CANCELLED", body.GetProperty("ownerNotice").GetProperty("status").GetString());
        Assert.DoesNotContain(noticeId, pendingIds);
        Assert.Null(jobResult);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseNotice notice = await db.Set<DeathCaseNotice>().AsNoTracking().SingleAsync(n => n.Id == noticeId);
            Assert.Equal(DeathCaseNoticeStatus.Cancelled, notice.Status);
            Assert.NotNull(notice.UpdatedAt);
            Assert.Equal(0, notice.AttemptCount);
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Null(stored.ObjectionStartsAt);
            Assert.Null(stored.ObjectionEndsAt);
        });
        JsonElement executorView = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);
        Assert.Equal("CANCELLED", executorView.GetProperty("ownerNotice").GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Death12_RejectWhenPlanNotDeathCaseOpenIsPlanStateInvalid()
    {
        // Chuẩn bị: kế hoạch bị đổi ở nơi khác sang ACTIVE
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, submitted.Vault.VaultId, PlanStatus.Active);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);

        // Kiểm tra: 409, hồ sơ vẫn đang xét, không ghi gì
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("PLAN_STATE_INVALID", problem.GetProperty("reason").GetString());
        await AssertStillUnderReviewWithoutDecisionAsync(submitted.CaseId);
    }

    [SqlFact]
    public async Task Death12_RejectWhenStatusBeforeCaseMissingIsPlanStateInvalid()
    {
        // Chuẩn bị: hồ sơ không nhớ trạng thái kế hoạch trước đó
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetStatusBeforeCaseAsync(factory, submitted.CaseId, null);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("PLAN_STATE_INVALID", problem.GetProperty("reason").GetString());
        await AssertStillUnderReviewWithoutDecisionAsync(submitted.CaseId);
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
    }

    [SqlFact]
    public async Task Ac71_RepeatRejectReturnsOkWithOneDecision()
    {
        // Chuẩn bị: đã từ chối một lần
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string oldEtag = detail.GetProperty("etag").GetString()!;
        HttpResponseMessage first = await DeathCaseReviewTestData.RejectAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, RejectReason);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);

        // Hành động: gọi lại với etag đã cũ và lý do khác
        HttpResponseMessage repeat = await DeathCaseReviewTestData.RejectAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, "Lý do khác");
        JsonElement repeatBody = await ApiTestHelpers.ReadJsonAsync(repeat);

        // Kiểm tra: 200 trạng thái hiện tại, vẫn một dòng quyết định với lý do đầu
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal("REJECTED", repeatBody.GetProperty("status").GetString());
        Assert.Equal(firstBody.GetProperty("etag").GetString(), repeatBody.GetProperty("etag").GetString());
        Assert.Equal(1, repeatBody.GetProperty("decisions").GetArrayLength());
        Assert.Equal(RejectReason, repeatBody.GetProperty("decisions")[0].GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Ac71_SupplementAfterRejectIsCaseClosed()
    {
        // Chuẩn bị: hồ sơ đã bị từ chối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        JsonElement rejectedBody = await ApiTestHelpers.ReadJsonAsync(rejected);

        // Hành động: quyết định khác trên hồ sơ đã đóng (kể cả với etag mới nhất)
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, rejectedBody.GetProperty("etag").GetString(),
            "Thiếu trang", new[] { "FILES" });

        // Kiểm tra: 409 CASE_CLOSED, không thêm dòng
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(supplement, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("CASE_CLOSED", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death04_ExecutorDraftLockedAfterReject()
    {
        // Chuẩn bị: Executor đang soạn bản nháp số 2 thì hồ sơ bị từ chối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.CreateDraftVersionAsync(submitted);
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        JsonElement executorView = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);

        // Hành động
        HttpResponseMessage edit = await submitted.Executor.Client.PutAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/draft",
            new { etag = executorView.GetProperty("etag").GetString(), issuedBy = "UBND phường mới" });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(edit, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("CASE_CLOSED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Dms04_OwnerCanCheckInAfterReject()
    {
        // Chuẩn bị: kỳ điểm danh còn hạn; trong lúc hồ sơ đang xét Owner không điểm danh được
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddOpenPeriodAsync(factory, submitted.Vault.PlanId, 5);
        HttpResponseMessage blocked = await submitted.Vault.Client.PostAsync("/api/v1/check-ins", null);
        await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");

        // Hành động
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        HttpResponseMessage checkIn = await submitted.Vault.Client.PostAsync("/api/v1/check-ins", null);

        // Kiểm tra: kế hoạch ACTIVE trở lại nên điểm danh được
        Assert.True(checkIn.IsSuccessStatusCode, "Owner phải điểm danh được sau khi hồ sơ bị từ chối, nhận " + checkIn.StatusCode);
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.Active, plan.Status);
    }

    [SqlFact]
    public async Task Death01_RejectAllowedWhileExecutorHasDraft()
    {
        // Chuẩn bị: Executor có bản nháp mới hơn bản đang xét (duyệt bị chặn, nhưng từ chối thì vẫn được)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.CreateDraftVersionAsync(submitted);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("REJECTED", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("decisions")[0].GetProperty("versionNo").GetInt32());
    }

    [SqlFact]
    public async Task Death12_ExecutorSeesRejectReason()
    {
        // Chuẩn bị: hồ sơ đang xét thì rejectReason là null
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement before = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);
        Assert.Equal("UNDER_REVIEW", before.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("rejectReason").ValueKind);
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        // Hành động
        HttpResponseMessage response = await submitted.Executor.Client.GetAsync(DeathCaseApi.CaseUrl(submitted.CaseId));
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement after = JsonSerializer.Deserialize<JsonElement>(raw);

        // Kiểm tra: Executor thấy lý do nhưng không thấy gì về Verifier
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("REJECTED", after.GetProperty("status").GetString());
        Assert.Equal(RejectReason, after.GetProperty("rejectReason").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("supplementRequest").ValueKind);
        Assert.DoesNotContain(submitted.Verifier.User.Id.ToString(), raw);
        Assert.DoesNotContain(submitted.Verifier.User.Email, raw);
        Assert.DoesNotContain("verifier", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AssertStillUnderReviewWithoutDecisionAsync(Guid caseId)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == caseId);
            Assert.Equal(DeathCaseStatus.UnderReview, stored.Status);
            Assert.Null(stored.ClosedAt);
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == caseId));
            DeathCaseNotice notice = await db.Set<DeathCaseNotice>().AsNoTracking().SingleAsync(n => n.CaseId == caseId);
            Assert.Equal(DeathCaseNoticeStatus.Pending, notice.Status);
        });
    }

    /// <summary>Chạy job điểm danh cho một kỳ, giống CheckInMonitor (một scope riêng).</summary>
    private async Task RunCheckInJobAsync(Guid periodId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ICheckInJob job = scope.ServiceProvider.GetRequiredService<ICheckInJob>();
            await job.ProcessPeriodAsync(periodId, CancellationToken.None);
        }
    }

    private async Task<CheckInPeriod> LoadPeriodAsync(Guid periodId)
    {
        CheckInPeriod? period = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            period = await db.CheckInPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
        });
        return period!;
    }
}
