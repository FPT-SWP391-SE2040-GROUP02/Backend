using System.Net;
using System.Text.Json;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
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
/// Job giao Verifier cho hồ sơ trống và giao lại khi quá hạn hoặc Verifier mất quyền, trên SQL Server thật
/// (ASSIGN-02, ASSIGN-03, ASSIGN-04, ROLE-03, TIME-02, D2, D4, D5, AC-71). Không giả lập giờ: lùi DueAt bằng helper.
/// </summary>
public sealed class DeathCaseAssignmentJobTests : IAsyncLifetime
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
    public async Task Assign04_EmptyCaseGetsVerifierWhenOneBecomesAvailable()
    {
        // Chuẩn bị: hồ sơ đang xét nhưng chưa có người giữ và chưa có Verifier nào khả dụng
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);
        DeathCaseAssignmentOutcome withoutCandidate = await RunJobAsync(submitted.CaseId);
        DeathCaseReviewTestData.VerifierClient newVerifier = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        DeathCase before = await LoadCaseAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: giao cho người mới, hạn theo D1, không đổi UpdatedAt (D4)
        DeathCase after = await LoadCaseAsync(submitted.CaseId);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        DeathCaseAssignment active = SingleActive(rows);
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, withoutCandidate);
        Assert.Equal(DeathCaseAssignmentOutcome.Assigned, outcome);
        Assert.Equal(newVerifier.User.Id, active.VerifierUserId);
        Assert.Equal(DeathCaseAssignmentRules.ComputeDueAt(active.AssignedAt, after.ObjectionEndsAt), active.DueAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(2, rows.Count);
    }

    [SqlFact]
    public async Task Assign04_EmptySupplementRequiredCaseGetsVerifier()
    {
        // Chuẩn bị: hồ sơ chờ Executor bổ sung và không còn ai giữ (D2: vẫn phải có người phụ trách)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);
        DeathCaseReviewTestData.VerifierClient newVerifier = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra
        DeathCaseAssignment active = SingleActive(await LoadAssignmentsAsync(submitted.CaseId));
        Assert.Equal(DeathCaseAssignmentOutcome.Assigned, outcome);
        Assert.Equal(newVerifier.User.Id, active.VerifierUserId);
        Assert.Equal(DeathCaseStatus.SupplementRequired, (await LoadCaseAsync(submitted.CaseId)).Status);
    }

    [SqlFact]
    public async Task Assign04_OverdueCaseIsReassigned()
    {
        // Chuẩn bị: hai Verifier, người đang giữ quá hạn 1 giờ
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        Guid holderId = submitted.Verifier.User.Id;
        Guid otherId = await FindOtherVerifierIdAsync(holderId);
        await MakeOverdueAsync(submitted.CaseId);
        DeathCase before = await LoadCaseAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: cũ đóng với OVERDUE, mới cho người kia, đúng một dòng còn hiệu lực (unique index không vỡ khi
        // đóng và thêm trong một lần lưu), UpdatedAt đổi (D4)
        DeathCase after = await LoadCaseAsync(submitted.CaseId);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        DeathCaseAssignment active = SingleActive(rows);
        DeathCaseAssignment ended = rows[0];
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        Assert.Equal(2, rows.Count);
        Assert.Equal(holderId, ended.VerifierUserId);
        Assert.NotNull(ended.EndedAt);
        Assert.Equal(DeathCaseAssignmentRules.EndReasonOverdue, ended.EndReason);
        Assert.Equal(otherId, active.VerifierUserId);
        Assert.Equal(DeathCaseAssignmentRules.ComputeDueAt(active.AssignedAt, after.ObjectionEndsAt), active.DueAt);
        Assert.NotEqual(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(DeathCaseStatus.UnderReview, after.Status);
    }

    [SqlFact]
    public async Task Assign04_NotYetOverdueCaseIsUntouched()
    {
        // Chuẩn bị: hạn còn 48 giờ, Verifier đang hoạt động
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        Assert.Single(rows);
        Assert.Equal(submitted.AssignmentId, rows[0].Id);
        Assert.Null(rows[0].EndedAt);
    }

    [SqlFact]
    public async Task D2_OverdueSupplementRequiredCaseIsNotReassigned()
    {
        // Chuẩn bị: hồ sơ chờ Executor bổ sung, hạn đã qua 5 giờ nhưng Verifier vẫn hoạt động
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-5));

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: không giao lại vì quá hạn khi đang chờ Executor
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        Assert.Single(rows);
        Assert.Null(rows[0].EndedAt);
    }

    [SqlFact]
    public async Task Assign02_DeactivatedVerifierIsReplacedBeforeDue()
    {
        // Chuẩn bị: hạn còn 48 giờ nhưng Admin vừa khóa Verifier đang giữ
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        Guid otherId = await FindOtherVerifierIdAsync(submitted.Verifier.User.Id);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        Assert.Equal(DeathCaseAssignmentRules.EndReasonVerifierUnavailable, rows[0].EndReason);
        Assert.Equal(otherId, SingleActive(rows).VerifierUserId);
    }

    [SqlFact]
    public async Task Assign02_DemotedVerifierIsReplacedEvenWhenSupplementRequired()
    {
        // Chuẩn bị: hồ sơ chờ bổ sung, Verifier đang giữ bị thu hồi vai trò
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        Guid otherId = await FindOtherVerifierIdAsync(submitted.Verifier.User.Id);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        await DeathCaseReviewTestData.SetUserRoleAsync(factory, submitted.Verifier.User.Id, UserRole.User);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        Assert.Equal(DeathCaseAssignmentRules.EndReasonVerifierUnavailable, rows[0].EndReason);
        Assert.Equal(otherId, SingleActive(rows).VerifierUserId);
    }

    [SqlFact]
    public async Task Assign04_NoCandidateKeepsCurrentVerifier()
    {
        // Chuẩn bị: chỉ một Verifier, đã quá hạn. Đặt RemindedDueAt = DueAt để không có việc nhắc xen vào kết quả.
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddHours(-1);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, dueAt);
        await DeathCaseReviewTestData.SetActiveRemindedDueAtAsync(factory, submitted.CaseId, dueAt);
        DeathCase before = await LoadCaseAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: không đổi gì
        DeathCase after = await LoadCaseAsync(submitted.CaseId);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, outcome);
        Assert.Single(rows);
        Assert.Equal(submitted.AssignmentId, rows[0].Id);
        Assert.Null(rows[0].EndedAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [SqlFact]
    public async Task Role03_ReassignSkipsOwnerExecutorSubmitterAndCurrentVerifier()
    {
        // Chuẩn bị: Owner và Executor (cũng là người gửi) đều được cấp vai trò Verifier; chỉ người đang giữ quá hạn
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetUserRoleAsync(factory, submitted.Vault.Owner.Id, UserRole.Verifier);
        await DeathCaseReviewTestData.SetUserRoleAsync(factory, submitted.Executor.User.Id, UserRole.Verifier);
        await MakeOverdueAsync(submitted.CaseId);
        DeathCaseAssignmentOutcome withoutCandidate = await RunJobAsync(submitted.CaseId);
        DeathCaseReviewTestData.VerifierClient fresh = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: lần đầu không ai hợp lệ, lần sau chọn đúng người mới
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        DeathCaseAssignment active = SingleActive(rows);
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, withoutCandidate);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        Assert.Equal(fresh.User.Id, active.VerifierUserId);
        Assert.NotEqual(submitted.Vault.Owner.Id, active.VerifierUserId);
        Assert.NotEqual(submitted.Executor.User.Id, active.VerifierUserId);
        Assert.NotEqual(submitted.Verifier.User.Id, active.VerifierUserId);
    }

    [SqlFact]
    public async Task Assign04_TwoVerifiersCanSwapBackAndForth()
    {
        // Chuẩn bị: nhóm chỉ có hai Verifier A và B
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        Guid verifierA = submitted.Verifier.User.Id;
        Guid verifierB = await FindOtherVerifierIdAsync(verifierA);

        // Hành động: A quá hạn → B; B quá hạn → A
        await MakeOverdueAsync(submitted.CaseId);
        DeathCaseAssignmentOutcome first = await RunJobAsync(submitted.CaseId);
        await MakeOverdueAsync(submitted.CaseId);
        DeathCaseAssignmentOutcome second = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: người từng giữ vẫn được giao lại (chỉ loại người đang giữ)
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, first);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, second);
        Assert.Equal(3, rows.Count);
        Assert.Equal(verifierA, rows[0].VerifierUserId);
        Assert.Equal(verifierB, rows[1].VerifierUserId);
        Assert.Equal(verifierA, rows[2].VerifierUserId);
        Assert.Equal(verifierA, SingleActive(rows).VerifierUserId);
    }

    [SqlFact]
    public async Task Assign03_OldVerifierGetsNotFoundAfterReassign()
    {
        // Chuẩn bị: Verifier cũ đã yêu cầu bổ sung, Executor gửi lại, rồi hồ sơ quá hạn và bị giao lại
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Thiếu nơi cấp", new[] { "ISSUED_BY" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);
        await DeathCaseReviewTestData.ResubmitAsync(submitted);
        await MakeOverdueAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);
        HttpResponseMessage oldView = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));

        // Kiểm tra: Verifier cũ không còn thấy hồ sơ; quyết định cũ vẫn còn, gắn phân công đã kết thúc (ASSIGN-03)
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
        Assert.Equal(HttpStatusCode.NotFound, oldView.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<DeathCaseDecision> decisions = await db.Set<DeathCaseDecision>().AsNoTracking()
                .Where(d => d.CaseId == submitted.CaseId).ToListAsync();
            DeathCaseDecision decision = Assert.Single(decisions);
            Assert.Equal(DeathCaseDecisionKind.SupplementRequested, decision.Kind);
            Assert.Equal(submitted.AssignmentId, decision.AssignmentId);
            Assert.Equal(submitted.Verifier.User.Id, decision.VerifierUserId);
            DeathCaseAssignment oldRow = await db.Set<DeathCaseAssignment>().AsNoTracking()
                .SingleAsync(a => a.Id == submitted.AssignmentId);
            Assert.NotNull(oldRow.EndedAt);
        });
    }

    [SqlFact]
    public async Task Assign03_Ac71_OldVerifierStaleEtagGetsEditConflict()
    {
        // Chuẩn bị: Verifier cũ lấy etag trước khi job chạy; một phiên khác của hệ thống đã đọc hồ sơ (kèm phân công cũ)
        // từ trước khi job commit, đúng tình huống Verifier cũ đang bấm nút lúc hồ sơ bị giao lại (D4)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string? oldEtag = detail.GetProperty("etag").GetString();
        await MakeOverdueAsync(submitted.CaseId);

        using (IServiceScope staleScope = factory.Services.CreateScope())
        {
            IDeathCaseRepository staleRepository = staleScope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            IUnitOfWork staleUnitOfWork = staleScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            DeathCase? stale = await staleRepository.FindByIdAsync(submitted.CaseId, CancellationToken.None);
            Assert.NotNull(stale);

            // Hành động: job giao lại và commit; sau đó phiên cũ cố ghi quyết định từ chối với dữ liệu đã đọc
            DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);
            stale.Status = DeathCaseStatus.Rejected;
            stale.UpdatedAt = DateTimeOffset.UtcNow;
            AppException conflict = await Assert.ThrowsAsync<AppException>(
                () => staleUnitOfWork.SaveChangesAsync(CancellationToken.None));

            // Kiểm tra: UpdatedAt là concurrency token nên lần ghi muộn bị chặn bằng EDIT_CONFLICT
            Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcome);
            Assert.Equal(ErrorCode.EditConflict, conflict.Code);
        }

        // Tải lại hoặc gửi lại etag cũ sau khi job đã commit: Verifier cũ không còn phân công nên nhận 404; hồ sơ vẫn đang xét
        HttpResponseMessage lateReject = await DeathCaseReviewTestData.RejectAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, "Không đủ căn cứ");
        Assert.Equal(HttpStatusCode.NotFound, lateReject.StatusCode);
        Assert.Equal(DeathCaseStatus.UnderReview, (await LoadCaseAsync(submitted.CaseId)).Status);
    }

    [SqlFact]
    public async Task Assign03_NewVerifierCanReviewAfterReassign()
    {
        // Chuẩn bị: hồ sơ quá hạn và được giao lại cho Verifier kia
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        Guid otherId = await FindOtherVerifierIdAsync(submitted.Verifier.User.Id);
        await MakeOverdueAsync(submitted.CaseId);
        await RunJobAsync(submitted.CaseId);
        User other = await LoadUserAsync(otherId);
        DeathCaseReviewTestData.VerifierClient newVerifier = DeathCaseReviewTestData.CreateClientFor(factory, other);

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(newVerifier.Client, submitted.CaseId);

        // Kiểm tra: GetDetailAsync đã kiểm 200; hồ sơ vẫn đang xét
        Assert.Equal("UNDER_REVIEW", detail.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Assign04_ClosedCaseIsIgnored()
    {
        // Chuẩn bị: phân công đã quá hạn nhưng hồ sơ đã đóng hoặc chưa gửi
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        await MakeOverdueAsync(submitted.CaseId);

        // Hành động và kiểm tra
        DeathCaseStatus[] closedStatuses =
        {
            DeathCaseStatus.ApprovedForHandover, DeathCaseStatus.Rejected, DeathCaseStatus.Draft,
        };
        foreach (DeathCaseStatus status in closedStatuses)
        {
            await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, status);
            DeathCaseAssignmentOutcome outcome = await RunJobAsync(submitted.CaseId);
            Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        }
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Single(rows);
        Assert.Null(rows[0].EndedAt);
    }

    [SqlFact]
    public async Task Time02_RunTwiceDoesNotReassignTwice()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        await MakeOverdueAsync(submitted.CaseId);

        // Hành động
        DeathCaseAssignmentOutcome first = await RunJobAsync(submitted.CaseId);
        DeathCaseAssignmentOutcome second = await RunJobAsync(submitted.CaseId);

        // Kiểm tra: lần hai không làm gì, vẫn một phân công đang hiệu lực
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, first);
        Assert.Equal(DeathCaseAssignmentOutcome.None, second);
        Assert.Equal(2, rows.Count);
        SingleActive(rows);
    }

    [SqlFact]
    public async Task Assign04_NeverApprovesOrRejects()
    {
        // Chuẩn bị: hồ sơ quá hạn, rồi người giữ mới bị khóa, rồi không còn ai thay được
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        await MakeOverdueAsync(submitted.CaseId);

        // Hành động: quá hạn → giao lại; khóa người mới → giao lại; khóa tất cả → không có người thay
        List<DeathCaseAssignmentOutcome> outcomes = new List<DeathCaseAssignmentOutcome>();
        outcomes.Add(await RunJobAsync(submitted.CaseId));
        DeathCaseAssignment second = SingleActive(await LoadAssignmentsAsync(submitted.CaseId));
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, second.VerifierUserId, UserStatus.Locked);
        outcomes.Add(await RunJobAsync(submitted.CaseId));
        DeathCaseAssignment third = SingleActive(await LoadAssignmentsAsync(submitted.CaseId));
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, third.VerifierUserId, UserStatus.Locked);
        outcomes.Add(await RunJobAsync(submitted.CaseId));

        // Kiểm tra: quá hạn không tự cho đạt hay từ chối; trạng thái, quyết định và kế hoạch giữ nguyên
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcomes[0]);
        Assert.Equal(DeathCaseAssignmentOutcome.Reassigned, outcomes[1]);
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, outcomes[2]);
        Assert.Equal(DeathCaseStatus.UnderReview, (await LoadCaseAsync(submitted.CaseId)).Status);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
    }

    [SqlFact]
    public async Task Assign04_ReminderSentTwelveHoursBeforeDue()
    {
        // Chuẩn bị: hạn còn 48 giờ nên chưa cần nhắc
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseAssignmentOutcome tooEarly = await RunJobWithSenderAsync(submitted.CaseId, sender);
        DateTimeOffset dueAt = await SetDueNotRemindedAsync(submitted.CaseId, 6);
        DeathCase before = await LoadCaseAsync(submitted.CaseId);

        // Hành động: hạn còn 6 giờ
        DeathCaseAssignmentOutcome outcome = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: thư chỉ có mã hồ sơ và hạn; ghi RemindedDueAt; không đổi UpdatedAt (D4) và không đổi phân công
        DeathCase after = await LoadCaseAsync(submitted.CaseId);
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.None, tooEarly);
        Assert.Equal(DeathCaseAssignmentOutcome.Reminded, outcome);
        DeathCaseVerifierReminder sent = Assert.Single(sender.Sent);
        Assert.Equal(submitted.AssignmentId, sent.AssignmentId);
        Assert.Equal(submitted.Verifier.User.Email, sent.ToEmail);
        Assert.Equal(DeathCaseRules.CaseCode(submitted.CaseId), sent.CaseCode);
        Assert.Equal(dueAt, sent.DueAt);
        Assert.Single(rows);
        Assert.Equal(dueAt, rows[0].RemindedDueAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [SqlFact]
    public async Task Time02_ReminderSentOncePerDueAt()
    {
        // Chuẩn bị
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await SetDueNotRemindedAsync(submitted.CaseId, 6);

        // Hành động
        DeathCaseAssignmentOutcome first = await RunJobWithSenderAsync(submitted.CaseId, sender);
        DeathCaseAssignmentOutcome second = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: chạy lại không gửi trùng
        Assert.Equal(DeathCaseAssignmentOutcome.Reminded, first);
        Assert.Equal(DeathCaseAssignmentOutcome.None, second);
        Assert.Single(sender.Sent);
    }

    [SqlFact]
    public async Task Assign04_ReminderSentAgainAfterResubmitMovesDueAt()
    {
        // Chuẩn bị: đã nhắc cho hạn còn 6 giờ; Verifier yêu cầu bổ sung; Executor gửi lại nên hạn dời tới +48 giờ
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await SetDueNotRemindedAsync(submitted.CaseId, 6);
        DeathCaseAssignmentOutcome firstReminder = await RunJobWithSenderAsync(submitted.CaseId, sender);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Thiếu nơi cấp", new[] { "ISSUED_BY" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);
        await DeathCaseReviewTestData.ResubmitAsync(submitted);
        DeathCaseAssignmentOutcome afterResubmit = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Hành động: hạn mới (khác hạn đã nhắc) lại còn 5 giờ; không đụng RemindedDueAt
        DateTimeOffset newDueAt = DateTimeOffset.UtcNow.AddHours(5);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, newDueAt);
        DeathCaseAssignmentOutcome secondReminder = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: nhắc lại cho hạn mới
        DeathCaseAssignment? active = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reminded, firstReminder);
        Assert.Equal(DeathCaseAssignmentOutcome.None, afterResubmit);
        Assert.Equal(DeathCaseAssignmentOutcome.Reminded, secondReminder);
        Assert.Equal(2, sender.Sent.Count);
        Assert.NotNull(active);
        Assert.Equal(newDueAt, active.RemindedDueAt);
    }

    [SqlFact]
    public async Task Ops02_ReminderFailureIsRetriedNextRound()
    {
        // Chuẩn bị: lần gửi đầu bị SMTP từ chối
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        sender.FailNext = true;
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await SetDueNotRemindedAsync(submitted.CaseId, 6);

        // Hành động
        DeathCaseAssignmentOutcome failed = await RunJobWithSenderAsync(submitted.CaseId, sender);
        DeathCaseAssignment? afterFailure = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        DeathCaseAssignmentOutcome retried = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: lỗi thì không lưu, vòng sau thử lại và thành công
        Assert.Equal(DeathCaseAssignmentOutcome.ReminderFailed, failed);
        Assert.NotNull(afterFailure);
        Assert.Null(afterFailure.RemindedDueAt);
        Assert.Equal(DeathCaseAssignmentOutcome.Reminded, retried);
        Assert.Equal(2, sender.CallCount);
        Assert.Single(sender.Sent);
    }

    [SqlFact]
    public async Task Assign04_OverdueWithoutCandidateStillWarnsCurrentVerifier()
    {
        // Chuẩn bị: chỉ một Verifier, hạn đã qua 1 giờ và chưa nhắc lần nào (không dùng MakeOverdueAsync)
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-1));
        await DeathCaseReviewTestData.SetActiveRemindedDueAtAsync(factory, submitted.CaseId, null);

        // Hành động
        DeathCaseAssignmentOutcome warned = await RunJobWithSenderAsync(submitted.CaseId, sender);
        DeathCaseAssignmentOutcome again = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: người đang giữ nhận cảnh báo quá hạn một lần, phân công không đổi
        List<DeathCaseAssignment> rows = await LoadAssignmentsAsync(submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.Reminded, warned);
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, again);
        Assert.Single(sender.Sent);
        Assert.Single(rows);
        Assert.Equal(submitted.AssignmentId, rows[0].Id);
        Assert.Null(rows[0].EndedAt);
    }

    [SqlFact]
    public async Task D2_SupplementRequiredGetsNoReminder()
    {
        // Chuẩn bị: hồ sơ chờ Executor bổ sung, hạn còn 6 giờ
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        await SetDueNotRemindedAsync(submitted.CaseId, 6);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra
        Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        Assert.Equal(0, sender.CallCount);
    }

    [SqlFact]
    public async Task Assign02_UnavailableVerifierGetsNoReminder()
    {
        // Chuẩn bị: Verifier đang giữ bị khóa, không có người thay, hạn còn 6 giờ
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);
        await SetDueNotRemindedAsync(submitted.CaseId, 6);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: không gửi thư cho người đã bị khóa
        Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, outcome);
        Assert.Equal(0, sender.CallCount);
    }

    [SqlFact]
    public async Task Assign04_VerifierWithoutEmailGetsNoReminder()
    {
        // Chuẩn bị: Verifier đang giữ có email rỗng, hạn còn 6 giờ
        FakeDeathCaseVerifierReminderSender sender = new FakeDeathCaseVerifierReminderSender();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid holderId = submitted.Verifier.User.Id;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == holderId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, string.Empty));
        });
        await SetDueNotRemindedAsync(submitted.CaseId, 6);

        // Hành động
        DeathCaseAssignmentOutcome outcome = await RunJobWithSenderAsync(submitted.CaseId, sender);

        // Kiểm tra: không gửi, không lưu RemindedDueAt
        DeathCaseAssignment? active = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        Assert.Equal(0, sender.CallCount);
        Assert.NotNull(active);
        Assert.Null(active.RemindedDueAt);
    }

    private async Task<DeathCaseAssignmentOutcome> RunJobAsync(Guid caseId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
            return await job.ProcessCaseAsync(caseId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Lùi hạn về 1 giờ trước và coi như đã nhắc cho hạn này (để test giao lại không bị bước nhắc xen vào).
    /// </summary>
    private async Task MakeOverdueAsync(Guid caseId)
    {
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddHours(-1);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, caseId, dueAt);
        await DeathCaseReviewTestData.SetActiveRemindedDueAtAsync(factory, caseId, dueAt);
    }

    /// <summary>Đặt hạn cách bây giờ hoursFromNow giờ và chưa nhắc lần nào (RemindedDueAt null). Trả hạn đã đặt.</summary>
    private async Task<DateTimeOffset> SetDueNotRemindedAsync(Guid caseId, double hoursFromNow)
    {
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddHours(hoursFromNow);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, caseId, dueAt);
        await DeathCaseReviewTestData.SetActiveRemindedDueAtAsync(factory, caseId, null);
        return dueAt;
    }

    /// <summary>Dựng job bằng tay từ các service trong scope, thay bộ gửi thư nhắc bằng bản giả.</summary>
    private async Task<DeathCaseAssignmentOutcome> RunJobWithSenderAsync(
        Guid caseId, IDeathCaseVerifierReminderSender sender)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IServiceProvider provider = scope.ServiceProvider;
            DeathCaseAssignmentJob job = new DeathCaseAssignmentJob(
                provider.GetRequiredService<IDeathCaseRepository>(),
                provider.GetRequiredService<IDeathCaseAssignmentRepository>(),
                provider.GetRequiredService<IVaultRepository>(),
                provider.GetRequiredService<IUserRepository>(),
                provider.GetRequiredService<IDeathCaseVerifierPicker>(),
                sender,
                provider.GetRequiredService<IUnitOfWork>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IObjectionRepository>(),
                provider.GetRequiredService<IIncidentService>());
            return await job.ProcessCaseAsync(caseId, CancellationToken.None);
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

    private async Task<User> LoadUserAsync(Guid userId)
    {
        User? result = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
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

    /// <summary>Chứng minh đúng một dòng còn hiệu lực (EndedAt null) rồi trả về dòng đó.</summary>
    private static DeathCaseAssignment SingleActive(List<DeathCaseAssignment> rows)
    {
        DeathCaseAssignment? active = null;
        int count = 0;
        foreach (DeathCaseAssignment row in rows)
        {
            if (row.EndedAt == null)
            {
                active = row;
                count++;
            }
        }
        Assert.Equal(1, count);
        return active!;
    }

    /// <summary>Verifier duy nhất còn lại khác người đang giữ (các test dùng đúng hai Verifier).</summary>
    private async Task<Guid> FindOtherVerifierIdAsync(Guid holderId)
    {
        Guid otherId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            otherId = await db.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Verifier && u.Id != holderId)
                .Select(u => u.Id)
                .SingleAsync();
        });
        return otherId;
    }
}
