using System.Net;
using System.Net.Http.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Job giao và giao lại Verifier cho hồ sơ Suspended có báo còn sống Open, trên SQL Server thật
/// (ALIVE-03, ALIVE-04, ALIVE-08, ASSIGN-03, ASSIGN-04, TIME-01, AC-64). Không giả lập giờ: lùi VerifierDueAt bằng truy vấn.
/// </summary>
public sealed class ObjectionVerifierJobTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private WebApplicationFactory<Program> app = null!;
    private readonly FakeDeathCaseVerifierReminderSender reminderSender = new FakeDeathCaseVerifierReminderSender();
    private readonly CapturingIncidentSender incidentSender = new CapturingIncidentSender();

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
                services.RemoveAll<IDeathCaseVerifierReminderSender>();
                services.AddSingleton<IDeathCaseVerifierReminderSender>(reminderSender);
                services.RemoveAll<IIncidentNoticeSender>();
                services.AddSingleton<IIncidentNoticeSender>(incidentSender);
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

    [SqlFact]
    public async Task Alive04_Report_VerifierDueIsSelfieDeadlinePlus48h()
    {
        // Chuẩn bị: hồ sơ đang xét có Verifier được giao
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        await ReportAsync(submitted);

        // Kiểm tra: hạn Verifier = hạn selfie + 48 giờ (D2a)
        DeathCaseObjection objection = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(submitted.Verifier.User.Id, objection.VerifierUserId);
        Assert.Equal(objection.SelfieDeadlineAt.AddHours(48), objection.VerifierDueAt);
    }

    [SqlFact]
    public async Task Alive08_OverdueVerifier_ReassignedWithNewDue()
    {
        // Chuẩn bị: Verifier thứ hai sẵn sàng; báo đã mở; Verifier hiện tại quá hạn; sự cố đã được nhắc một lần
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.VerifierClient second = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        DeathCaseObjection before = await LoadObjectionAsync(submitted.CaseId);
        await SetVerifierDueAtAsync(submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-1));
        await SetIncidentRemindedCountAsync(submitted.CaseId, 1);

        // Hành động
        List<Guid> picked = await FindCaseIdsAsync();
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra
        Assert.Contains(submitted.CaseId, picked);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(DeathCaseAssignmentRules.EndReasonOverdue, rows[0].EndReason);
        Assert.NotNull(rows[0].EndedAt);
        Assert.Null(rows[1].EndedAt);
        Assert.Equal(second.User.Id, rows[1].VerifierUserId);
        DeathCaseObjection after = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(second.User.Id, after.VerifierUserId);
        Assert.Equal(ObjectionRules.ComputeVerifierDueAt(rows[1].AssignedAt, after.SelfieDeadlineAt), after.VerifierDueAt);
        // ALIVE-03: hạn selfie của cả lời báo không đổi
        Assert.Equal(before.SelfieDeadlineAt, after.SelfieDeadlineAt);
        Incident incident = await LoadOwnerAliveIncidentAsync(submitted.CaseId);
        Assert.Equal(second.User.Id, incident.AssigneeUserId);
        Assert.Equal(0, incident.RemindedCount);
    }

    [SqlFact]
    public async Task Alive08_LockedVerifier_ReassignedBeforeDue()
    {
        // Chuẩn bị: báo đang mở, chưa quá hạn, nhưng Verifier đang giữ bị khóa
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.VerifierClient second = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentRules.EndReasonVerifierUnavailable, rows[0].EndReason);
        Assert.Equal(second.User.Id, rows[1].VerifierUserId);
        DeathCaseObjection objection = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(second.User.Id, objection.VerifierUserId);
    }

    [SqlFact]
    public async Task Alive08_NotOverdue_NotPickedAndUntouched()
    {
        // Chuẩn bị: báo đang mở, Verifier còn hoạt động và chưa quá hạn; có Verifier khác sẵn sàng
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        DeathCaseObjection before = await LoadObjectionAsync(submitted.CaseId);

        // Hành động
        List<Guid> picked = await FindCaseIdsAsync();
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: job không lấy hồ sơ, chạy trực tiếp cũng không làm gì
        Assert.DoesNotContain(submitted.CaseId, picked);
        Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Single(rows);
        Assert.Null(rows[0].EndedAt);
        DeathCaseObjection after = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(before.VerifierUserId, after.VerifierUserId);
        Assert.Equal(before.VerifierDueAt, after.VerifierDueAt);
    }

    [SqlFact]
    public async Task Alive08_NoCandidate_KeepsCurrentVerifier()
    {
        // Chuẩn bị: báo đang mở, Verifier quá hạn nhưng không có ai khác để giao (D5)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await ReportAsync(submitted);
        DateTimeOffset overdueAt = DateTimeOffset.UtcNow.AddHours(-1);
        await SetVerifierDueAtAsync(submitted.CaseId, overdueAt);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: giữ người cũ, không kết thúc phân công
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, outcome);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Single(rows);
        Assert.Null(rows[0].EndedAt);
        DeathCaseObjection objection = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(submitted.Verifier.User.Id, objection.VerifierUserId);
    }

    [SqlFact]
    public async Task Alive08_ObjectionWithoutVerifier_GetsAssigned()
    {
        // Chuẩn bị: báo khi chưa có ai giữ hồ sơ (phân công đã kết thúc, Verifier cũ bị khóa)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);
        await ReportAsync(submitted);
        DeathCaseObjection reported = await LoadObjectionAsync(submitted.CaseId);
        Assert.Null(reported.VerifierUserId);
        Assert.Null(reported.VerifierDueAt);
        List<Guid> picked = await FindCaseIdsAsync();
        DeathCaseAssignmentOutcome withoutCandidate = await RunJobAsync(submitted.CaseId);
        DeathCaseReviewTestData.VerifierClient newVerifier = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        DeathCase caseBefore = await LoadCaseAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: giao cho người mới, đặt hạn, không đổi UpdatedAt của hồ sơ (D4: không ai mất quyền)
        Assert.Contains(submitted.CaseId, picked);
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, withoutCandidate);
        Assert.Equal(DeathCaseAssignmentOutcome.Assigned, outcome);
        DeathCaseObjection objection = await LoadObjectionAsync(submitted.CaseId);
        DeathCaseAssignment active = await LoadActiveAssignmentAsync(submitted.CaseId);
        Assert.Equal(newVerifier.User.Id, objection.VerifierUserId);
        Assert.Equal(newVerifier.User.Id, active.VerifierUserId);
        Assert.Equal(ObjectionRules.ComputeVerifierDueAt(active.AssignedAt, objection.SelfieDeadlineAt), objection.VerifierDueAt);
        DeathCase caseAfter = await LoadCaseAsync(submitted.CaseId);
        Assert.Equal(caseBefore.UpdatedAt, caseAfter.UpdatedAt);
        Incident incident = await LoadOwnerAliveIncidentAsync(submitted.CaseId);
        Assert.Equal(newVerifier.User.Id, incident.AssigneeUserId);
    }

    [SqlFact]
    public async Task Alive08_JobNeverConcludesOrUnblocks()
    {
        // Chuẩn bị: báo đang mở, Verifier quá hạn, có người thay
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        await SetVerifierDueAtAsync(submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-1));
        int decisionsBefore = await CountDecisionsAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: vẫn chặn, báo vẫn mở, hold còn, không có quyết định mới
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        DeathCase deathCase = await LoadCaseAsync(submitted.CaseId);
        Assert.Equal(DeathCaseStatus.Suspended, deathCase.Status);
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.Blocked, plan.Status);
        DeathCaseObjection objection = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(ObjectionStatus.Open, objection.Status);
        Assert.Null(objection.Conclusion);
        int activeHolds = await CountActiveOwnerAliveHoldsAsync(submitted.Vault.VaultId);
        Assert.Equal(1, activeHolds);
        int decisionsAfter = await CountDecisionsAsync(submitted.CaseId);
        Assert.Equal(decisionsBefore, decisionsAfter);
    }

    [SqlFact]
    public async Task Alive08_Assign03_OldVerifierCannotConclude_NewVerifierCan()
    {
        // Chuẩn bị: job đã giao lại hồ sơ bị phản đối cho Verifier mới
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.VerifierClient second = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        await SetVerifierDueAtAsync(submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-1));
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);

        // Hành động
        HttpResponseMessage oldVerifier = await submitted.Verifier.Client.PostAsJsonAsync(
            ConcludeUrl(submitted.CaseId), new { conclusion = "OBJECTION_REJECTED", reason = "Đã xem xét hồ sơ" });
        HttpResponseMessage newVerifier = await second.Client.PostAsJsonAsync(
            ConcludeUrl(submitted.CaseId), new { conclusion = "OBJECTION_REJECTED", reason = "Đã xem xét hồ sơ" });

        // Kiểm tra: Verifier cũ mất quyền (404), Verifier mới kết luận được
        Assert.Equal(HttpStatusCode.NotFound, oldVerifier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newVerifier.StatusCode);
    }

    [SqlFact]
    public async Task Alive08_NoSeparateReminderEmail()
    {
        // Chuẩn bị: báo đang mở, hạn Verifier còn 6 giờ (trong cửa sổ nhắc 12 giờ của hồ sơ thường)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        await SetVerifierDueAtAsync(submitted.CaseId, DateTimeOffset.UtcNow.AddHours(6));

        // Hành động
        List<Guid> picked = await FindCaseIdsAsync();
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: không lấy hồ sơ, không gửi thư nhắc riêng (IncidentJob nhắc qua sự cố OWNER_ALIVE)
        Assert.DoesNotContain(submitted.CaseId, picked);
        Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        Assert.Equal(0, reminderSender.CallCount);
    }

    [SqlFact]
    public async Task Alive08_ConcludedObjection_Ignored()
    {
        // Chuẩn bị: Verifier bác phản đối → báo đã kết luận, hồ sơ về luồng thường
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        HttpResponseMessage concluded = await submitted.Verifier.Client.PostAsJsonAsync(
            ConcludeUrl(submitted.CaseId), new { conclusion = "OBJECTION_REJECTED", reason = "Đã xem xét hồ sơ" });
        Assert.Equal(HttpStatusCode.OK, concluded.StatusCode);
        await SetVerifierDueAtAsync(submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-1));
        DeathCaseObjection before = await LoadObjectionAsync(submitted.CaseId);

        // Hành động: job chạy khi hồ sơ đã về luồng thường, rồi chạy khi hồ sơ (giả lập) vẫn Suspended mà báo đã đóng
        DeathCaseAssignmentOutcome backToNormal = await RunJobAsync(submitted.CaseId);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.Suspended);
        List<Guid> picked = await FindCaseIdsAsync();
        DeathCaseAssignmentOutcome suspendedButConcluded = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: không giao lại theo luồng phản đối, báo không bị sửa
        Assert.Equal(DeathCaseAssignmentOutcome.None, backToNormal);
        Assert.DoesNotContain(submitted.CaseId, picked);
        Assert.Equal(DeathCaseAssignmentOutcome.None, suspendedButConcluded);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Single(rows);
        DeathCaseObjection after = await LoadObjectionAsync(submitted.CaseId);
        Assert.Equal(ObjectionStatus.Concluded, after.Status);
        Assert.Equal(before.VerifierUserId, after.VerifierUserId);
        Assert.Equal(before.VerifierDueAt, after.VerifierDueAt);
    }

    [SqlFact]
    public async Task Alive08_IncidentJob_RemindsNewVerifierAfter48h()
    {
        // Chuẩn bị: job đã giao lại cho Verifier mới; hạn nhắc của sự cố OWNER_ALIVE đã tới
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.VerifierClient second = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await ReportAsync(submitted);
        await SetVerifierDueAtAsync(submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-1));
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        Incident incident = await LoadOwnerAliveIncidentAsync(submitted.CaseId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<Incident>().Where(i => i.Id == incident.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });

        // Hành động
        string? result;
        using (IServiceScope scope = app.Services.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<IIncidentJob>().ProcessAsync(incident.Id, CancellationToken.None);
        }

        // Kiểm tra: thư nhắc gửi cho Verifier mới, không phải Verifier cũ
        Assert.Equal("REMINDED", result);
        IncidentNotice notice = Assert.Single(incidentSender.Sent);
        Assert.Equal(IncidentNoticeKind.Reminder, notice.Kind);
        Assert.Equal(second.User.Email, notice.ToEmail);
        Assert.NotEqual(submitted.Verifier.User.Email, notice.ToEmail);
        // Verifier không vào được /admin: link trong thư dẫn về trang Verifier.
        Assert.Equal(IncidentRecipientArea.Verifier, notice.Area);
    }

    private static string ConcludeUrl(Guid caseId)
    {
        return $"/api/v1/verifier/death-cases/{caseId}/objection/conclude";
    }

    /// <summary>Owner báo "Tôi còn sống" qua API (hồ sơ phải CanReport).</summary>
    private async Task ReportAsync(DeathCaseReviewTestData.SubmittedCase submitted)
    {
        HttpResponseMessage response = await submitted.Vault.Client.PostAsync(
            $"/api/v1/vaults/{submitted.Vault.VaultId}/death-case/objection", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<Guid>> FindCaseIdsAsync()
    {
        using (IServiceScope scope = app.Services.CreateScope())
        {
            IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
            return await job.FindCaseIdsAsync(CancellationToken.None);
        }
    }

    private async Task<DeathCaseAssignmentOutcome> RunJobAsync(Guid caseId)
    {
        using (IServiceScope scope = app.Services.CreateScope())
        {
            IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
            return await job.ProcessCaseAsync(caseId, CancellationToken.None);
        }
    }

    private Task SetVerifierDueAtAsync(Guid caseId, DateTimeOffset? dueAt)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == caseId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.VerifierDueAt, dueAt));
        });
    }

    private Task SetIncidentRemindedCountAsync(Guid caseId, int count)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<Incident>().Where(i => i.Type == IncidentType.OwnerAlive && i.TargetId == caseId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.RemindedCount, count));
        });
    }

    private async Task<DeathCaseObjection> LoadObjectionAsync(Guid caseId)
    {
        DeathCaseObjection? result = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseObjection>().AsNoTracking().SingleAsync(o => o.CaseId == caseId);
        });
        return result!;
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

    /// <summary>Mọi phân công của hồ sơ (kể cả đã kết thúc), cũ nhất trước.</summary>
    private async Task<List<DeathCaseAssignment>> LoadAssignmentsAsync(Guid caseId)
    {
        List<DeathCaseAssignment> rows = new List<DeathCaseAssignment>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            rows = await db.Set<DeathCaseAssignment>().AsNoTracking()
                .Where(a => a.CaseId == caseId)
                .OrderBy(a => a.AssignedAt)
                .ThenBy(a => a.Id)
                .ToListAsync();
        });
        return rows;
    }

    private async Task<DeathCaseAssignment> LoadActiveAssignmentAsync(Guid caseId)
    {
        DeathCaseAssignment? active = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, caseId);
        Assert.NotNull(active);
        return active!;
    }

    private async Task<Incident> LoadOwnerAliveIncidentAsync(Guid caseId)
    {
        Incident? result = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<Incident>().AsNoTracking()
                .SingleAsync(i => i.Type == IncidentType.OwnerAlive && i.TargetId == caseId);
        });
        return result!;
    }

    private async Task<int> CountDecisionsAsync(Guid caseId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == caseId);
        });
        return count;
    }

    private async Task<int> CountActiveOwnerAliveHoldsAsync(Guid vaultId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<Hold>().CountAsync(h => h.VaultId == vaultId
                && h.Type == HoldType.OwnerAlive
                && h.Status == HoldStatus.Active);
        });
        return count;
    }
}
