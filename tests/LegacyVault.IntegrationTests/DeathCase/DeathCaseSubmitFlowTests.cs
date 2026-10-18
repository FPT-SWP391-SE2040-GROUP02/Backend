using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Executor gửi hồ sơ chứng tử qua HTTP trên SQL Server thật: khóa kế hoạch, cờ, thông báo Owner, phân công Verifier
/// (DEATH-01, 04, 05, 08, 10, 11, ASSIGN-01, ROLE-03, SETUP-05, DMS-04, AC-63, AC-67, AC-71).
/// </summary>
public sealed class DeathCaseSubmitFlowTests : IAsyncLifetime
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
    public async Task Death05_SubmitLocksPlanAndKeepsStatusBeforeCase()
    {
        // Chuẩn bị: Owner quá hạn điểm danh (đang trong thời gian chờ), Executor đã soạn đủ hồ sơ
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        Assert.Empty(DeathCaseApi.ReadStrings(prepared.GetProperty("missingRequirements")));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
        Assert.Equal(1, submitted.GetProperty("reviewVersionNo").GetInt32());
        Assert.False(submitted.GetProperty("currentVersion").GetProperty("isDraft").GetBoolean());
        Assert.NotEqual(prepared.GetProperty("etag").GetString(), submitted.GetProperty("etag").GetString());
        Guid caseId = submitted.GetProperty("id").GetGuid();
        DeathCase deathCase = await LoadCaseAsync(caseId);
        Assert.Equal(PlanStatus.CheckinOverdue, deathCase.StatusBeforeCase);
        Assert.NotNull(deathCase.FirstSubmittedAt);
        Assert.Equal(1, deathCase.ReviewVersionNo);
        Assert.Equal(PlanStatus.DeathCaseOpen, await LoadPlanStatusAsync(vault.VaultId));
        DeathCaseVersion version = await LoadVersionAsync(caseId, 1);
        Assert.NotNull(version.SubmittedAt);
        Assert.Equal(executor.User.Id, version.SubmittedByUserId);
        Assert.NotNull(version.PlanConfigVersion);
    }

    [SqlFact]
    public async Task Setup05_OwnerCannotEditContainerAfterSubmit()
    {
        // Chuẩn bị: hồ sơ đã gửi, kế hoạch khóa
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Hành động
        HttpResponseMessage response = await vault.Client.PostAsJsonAsync(
            $"/api/v1/vaults/{vault.VaultId}/containers", new { name = "Gói mới" });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("PLAN_LOCKED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Dms04_OwnerCheckInIsRejectedAfterSubmit()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Hành động
        HttpResponseMessage response = await vault.Client.PostAsync("/api/v1/check-ins", null);

        // Kiểm tra: điểm danh không đưa kế hoạch về ACTIVE
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("DEATH_CASE_OPEN", problem.GetProperty("planStatus").GetString());
        Assert.Equal(PlanStatus.DeathCaseOpen, await LoadPlanStatusAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Death05_SubmitAndCheckInJobDoNotOverwritePlanStatus()
    {
        // Chuẩn bị: kế hoạch đang chạy, có kỳ điểm danh đã quá hạn
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, now.AddDays(-31), null);
        Guid periodId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            periodId = await db.CheckInPeriods.Where(p => p.PlanId == vault.PlanId).Select(p => p.Id).SingleAsync();
        });
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        using (IServiceScope jobScope = factory.Services.CreateScope())
        {
            // Job điểm danh đọc kỳ và kế hoạch (còn ACTIVE) trước khi hồ sơ được gửi...
            ICheckInRepository checkInRepository = jobScope.ServiceProvider.GetRequiredService<ICheckInRepository>();
            IUnitOfWork jobUnitOfWork = jobScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            CheckInPeriod? period = await checkInRepository.FindPeriodForJobAsync(periodId, CancellationToken.None);
            Assert.NotNull(period);
            Assert.Equal(PlanStatus.Active, period.Plan.Status);

            // ...Executor gửi hồ sơ, kế hoạch sang DEATH_CASE_OPEN...
            await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

            // Hành động: ...rồi job lưu theo kiểu MoveToNextStatus (ACTIVE → CHECKIN_OVERDUE)
            period.OverdueAt = now;
            period.Plan.Status = PlanStatus.CheckinOverdue;
            period.Plan.UpdatedAt = now;
            AppException conflict = await Assert.ThrowsAsync<AppException>(
                () => jobUnitOfWork.SaveChangesAsync(CancellationToken.None));

            // Kiểm tra: RowVersion của kế hoạch chặn ghi đè
            Assert.Equal(ErrorCode.EditConflict, conflict.Code);
        }
        Assert.Equal(PlanStatus.DeathCaseOpen, await LoadPlanStatusAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Death01_IncompleteSubmitListsMissingParts()
    {
        // Chuẩn bị: mới khai họ tên, chưa có gì khác
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        Guid caseId = opened.GetProperty("id").GetGuid();
        HttpResponseMessage edit = await executor.Client.PutAsJsonAsync(
            DeathCaseApi.CaseUrl(caseId) + "/draft",
            new { etag = opened.GetProperty("etag").GetString(), ownerFullName = "Nguyễn Văn An" });
        JsonElement edited = await ApiTestHelpers.ReadJsonAsync(edit);

        // Hành động
        HttpResponseMessage response = await DeathCaseApi.SubmitAsync(
            executor.Client, caseId, edited.GetProperty("etag").GetString());

        // Kiểm tra: không khóa gì cả
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_INCOMPLETE");
        Assert.Equal(new List<string>
        {
            "OWNER_CITIZEN_ID", "DATE_OF_DEATH", "CERTIFICATE_NUMBER", "ISSUED_ON", "ISSUED_BY", "FILES",
        }, DeathCaseApi.ReadStrings(problem.GetProperty("missingRequirements")));
        Assert.Equal(PlanStatus.Active, await LoadPlanStatusAsync(vault.VaultId));
        DeathCase deathCase = await LoadCaseAsync(caseId);
        Assert.Equal(DeathCaseStatus.Draft, deathCase.Status);
        Assert.Null(deathCase.FirstSubmittedAt);
    }

    [SqlFact]
    public async Task Death05_SubmitWhenPlanBackToDraftIsRejected()
    {
        // Chuẩn bị: hồ sơ đủ, nhưng Owner đã đưa kế hoạch về nháp trong lúc Executor soạn
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.Draft);

        // Hành động
        HttpResponseMessage response = await DeathCaseApi.SubmitAsync(
            executor.Client, prepared.GetProperty("id").GetGuid(), prepared.GetProperty("etag").GetString());

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("PLAN_NOT_RUNNING", problem.GetProperty("reason").GetString());
        Assert.Equal("DRAFT", problem.GetProperty("planStatus").GetString());
        Assert.Equal(PlanStatus.Draft, await LoadPlanStatusAsync(vault.VaultId));
        DeathCase deathCase = await LoadCaseAsync(prepared.GetProperty("id").GetGuid());
        Assert.Equal(DeathCaseStatus.Draft, deathCase.Status);
    }

    [SqlFact]
    public async Task Death01_SubmitRequiresConfirmation()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        HttpResponseMessage response = await DeathCaseApi.SubmitAsync(
            executor.Client, prepared.GetProperty("id").GetGuid(), prepared.GetProperty("etag").GetString(), confirm: false);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("confirm", out _));
        Assert.Equal(PlanStatus.Active, await LoadPlanStatusAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Ac63_Death10_CheckInAfterDeathDateRaisesFlag()
    {
        // Chuẩn bị: ngày mất cách đây 3 ngày nhưng Owner còn điểm danh hôm qua
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, now.AddDays(-20), now.AddDays(-1));
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra: có cờ, hồ sơ vẫn gửi được (Verifier xem xét sau); chi tiết cờ không lộ dữ liệu nhạy cảm
        Guid caseId = submitted.GetProperty("id").GetGuid();
        List<DeathCaseFlag> flags = await LoadFlagsAsync(caseId);
        DeathCaseFlag flag = Assert.Single(flags);
        Assert.Equal(DeathCaseFlagKind.CheckinAfterDeath, flag.Kind);
        Assert.Equal((await LoadVersionAsync(caseId, 1)).Id, flag.VersionId);
        Assert.DoesNotContain(DeathCaseApi.CitizenId, flag.Detail);
        Assert.Null(flag.ReviewedAt);
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Death10_CheckInOnDeathDayRaisesNoFlag()
    {
        // Chuẩn bị: Owner điểm danh trưa đúng ngày mất (giờ Việt Nam) — vẫn trước 00:00 ngày kế tiếp
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DateOnly deathDay = DeathCaseApi.Today().AddDays(-3);
        DateTimeOffset noonOnDeathDay = new DateTimeOffset(deathDay.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7));
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, noonOnDeathDay.AddDays(-20), noonOnDeathDay);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(executor.Client, vault.VaultId, deathDay);

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra
        List<DeathCaseFlag> flags = await LoadFlagsAsync(submitted.GetProperty("id").GetGuid());
        Assert.Empty(flags);
    }

    [SqlFact]
    public async Task Death08_SubmitQueuesOwnerNoticeWithoutStartingClock()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra: một thông báo Pending cho Owner, đồng hồ 72 giờ chưa chạy
        Guid caseId = submitted.GetProperty("id").GetGuid();
        List<DeathCaseNotice> notices = await LoadNoticesAsync(caseId);
        DeathCaseNotice notice = Assert.Single(notices);
        Assert.Equal(DeathCaseNoticeStatus.Pending, notice.Status);
        Assert.Equal(1, notice.VersionNo);
        Assert.Equal(vault.Owner.Id, notice.RecipientUserId);
        Assert.Equal(DeathCaseNoticeChannel.Email, notice.Channel);
        Assert.Equal(0, notice.AttemptCount);
        DeathCase deathCase = await LoadCaseAsync(caseId);
        Assert.Equal(1, deathCase.NotifiedVersionNo);
        Assert.Null(deathCase.ObjectionStartsAt);
        Assert.Null(deathCase.ObjectionEndsAt);
        Assert.Equal("PENDING", submitted.GetProperty("ownerNotice").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("objectionEndsAt").ValueKind);
    }

    [SqlFact]
    public async Task Death08_NoticeJobAfterSubmitStartsSeventyTwoHours()
    {
        // Chuẩn bị: hồ sơ đã gửi, thông báo đang chờ
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);
        Guid caseId = submitted.GetProperty("id").GetGuid();

        // Hành động: chạy job gửi thông báo (bộ gửi chỉ ghi log vì chưa cấu hình email → coi là đã giao)
        DeathCaseNoticeStatus? status = null;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
            List<Guid> pending = await job.FindPendingNoticeIdsAsync(CancellationToken.None);
            Guid noticeId = Assert.Single(pending);
            status = await job.ProcessNoticeAsync(noticeId, CancellationToken.None);
        }

        // Kiểm tra
        Assert.Equal(DeathCaseNoticeStatus.Delivered, status);
        JsonElement current = await ApiTestHelpers.ReadJsonAsync(
            await executor.Client.GetAsync(DeathCaseApi.CaseUrl(caseId)));
        JsonElement ownerNotice = current.GetProperty("ownerNotice");
        Assert.Equal("DELIVERED", ownerNotice.GetProperty("status").GetString());
        DateTimeOffset deliveredAt = ownerNotice.GetProperty("deliveredAt").GetDateTimeOffset();
        DateTimeOffset objectionEndsAt = current.GetProperty("objectionEndsAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(72), objectionEndsAt - deliveredAt);
        DeathCase deathCase = await LoadCaseAsync(caseId);
        Assert.Equal(deliveredAt, deathCase.ObjectionStartsAt);
    }

    [SqlFact]
    public async Task Assign01_Role03_SubmitAssignsVerifierNotOwnerOrExecutor()
    {
        // Chuẩn bị: Owner và người gửi cũng mang vai trò Verifier nhưng phải bị loại; chỉ một Verifier hợp lệ
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await SetRoleAsync(vault.Owner.Id, UserRole.Verifier);
        await SetRoleAsync(executor.User.Id, UserRole.Verifier);
        User eligible = await DeathCaseTestData.AddVerifierAsync(factory);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra: phân công đúng người, hạn 48 giờ; response không lộ Verifier
        Guid caseId = submitted.GetProperty("id").GetGuid();
        DeathCaseAssignment assignment = Assert.Single(await LoadAssignmentsAsync(caseId));
        Assert.Equal(eligible.Id, assignment.VerifierUserId);
        Assert.Equal(TimeSpan.FromHours(48), assignment.DueAt - assignment.AssignedAt);
        Assert.Null(assignment.EndedAt);
        Assert.DoesNotContain(eligible.Id.ToString(), submitted.GetRawText());
    }

    [SqlFact]
    public async Task Ac67_VerifierWhoIsExecutorOfVaultIsNotAssigned()
    {
        // Chuẩn bị: Executor dự phòng cũng là Verifier (chưa từng được giao) — phải bị loại dù xoay vòng sẽ chọn trước
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        User backupVerifier = await DeathCaseTestData.AddVerifierAsync(factory);
        await DeathCaseTestData.AddAcceptedExecutorAsync(factory, vault.VaultId, backupVerifier.Id, ExecutorRole.Backup);
        User outsider = await DeathCaseTestData.AddVerifierAsync(factory);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra
        DeathCaseAssignment assignment = Assert.Single(await LoadAssignmentsAsync(submitted.GetProperty("id").GetGuid()));
        Assert.Equal(outsider.Id, assignment.VerifierUserId);
        Assert.NotEqual(backupVerifier.Id, assignment.VerifierUserId);
    }

    [SqlFact]
    public async Task Assign04_NoVerifierLeavesCaseUnassigned()
    {
        // Chuẩn bị: hệ thống chưa có Verifier nào
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra: vẫn gửi thành công, hồ sơ chờ được giao
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
        Assert.Empty(await LoadAssignmentsAsync(submitted.GetProperty("id").GetGuid()));
    }

    [SqlFact]
    public async Task Ac71_DoubleSubmitKeepsOneSubmittedVersion()
    {
        // Chuẩn bị: hai Executor, một Verifier
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient first = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCaseTestData.ExecutorClient second = await DeathCaseTestData.CreateExecutorClientAsync(
            factory, vault.VaultId, ExecutorRole.Backup);
        await DeathCaseTestData.AddVerifierAsync(factory);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            first.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        Guid caseId = prepared.GetProperty("id").GetGuid();
        string etag = prepared.GetProperty("etag").GetString()!;

        // Hành động: hai Executor bấm "Gửi" cùng lúc với cùng etag, rồi bấm lại lần nữa
        Task<HttpResponseMessage> firstTask = DeathCaseApi.SubmitAsync(first.Client, caseId, etag);
        Task<HttpResponseMessage> secondTask = DeathCaseApi.SubmitAsync(second.Client, caseId, etag);
        HttpResponseMessage[] responses = await Task.WhenAll(firstTask, secondTask);
        HttpResponseMessage again = await DeathCaseApi.SubmitAsync(first.Client, caseId, etag);

        // Kiểm tra: cả hai nhận trạng thái "đang xét"; DB chỉ có một lần gửi
        foreach (HttpResponseMessage response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
            Assert.Equal("UNDER_REVIEW", body.GetProperty("status").GetString());
        }
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, await CountSubmittedVersionsAsync(caseId));
        Assert.Single(await LoadNoticesAsync(caseId));
        Assert.Single(await LoadAssignmentsAsync(caseId));
        Assert.Equal(PlanStatus.DeathCaseOpen, await LoadPlanStatusAsync(vault.VaultId));
        DeathCase deathCase = await LoadCaseAsync(caseId);
        Assert.Equal(PlanStatus.Active, deathCase.StatusBeforeCase);
        Assert.Equal(1, deathCase.ReviewVersionNo);
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

    private async Task<PlanStatus> LoadPlanStatusAsync(Guid vaultId)
    {
        PlanStatus status = PlanStatus.Closed;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            status = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Status).SingleAsync();
        });
        return status;
    }

    private async Task<List<DeathCaseFlag>> LoadFlagsAsync(Guid caseId)
    {
        List<DeathCaseFlag> result = new List<DeathCaseFlag>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseFlag>().AsNoTracking().Where(f => f.CaseId == caseId).ToListAsync();
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

    private async Task<List<DeathCaseAssignment>> LoadAssignmentsAsync(Guid caseId)
    {
        List<DeathCaseAssignment> result = new List<DeathCaseAssignment>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseAssignment>().AsNoTracking().Where(a => a.CaseId == caseId).ToListAsync();
        });
        return result;
    }

    private async Task<int> CountSubmittedVersionsAsync(Guid caseId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<DeathCaseVersion>().CountAsync(v => v.CaseId == caseId && v.SubmittedAt != null);
        });
        return count;
    }

    private async Task SetRoleAsync(Guid userId, UserRole role)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, role));
        });
    }
}
