using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Verifier duyệt hồ sơ chứng tử và mở bàn giao F3 qua IHandoverStarter, trên SQL Server thật
/// (DEATH-03, 04, 06, 08, 09, 10, ROLE-03, STATE-03, AC-10, AC-63, AC-67, AC-71).
/// </summary>
public sealed class VerifierApproveTests : IAsyncLifetime
{
    private const string ApproveReason = "Giấy chứng tử hợp lệ, thông tin khớp.";

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
    public async Task Ac10_Death06_ApproveOpensHandoverWithoutGrant()
    {
        // Chuẩn bị: Owner đã nhận thư và hết 72 giờ; gói READY có đúng một người nhận
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, "  " + ApproveReason + "  ");
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra: hồ sơ đã duyệt, kế hoạch sang bàn giao, có lượt và snapshot, KHÔNG có grant
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("APPROVED_FOR_HANDOVER", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("closedAt").ValueKind);
        Assert.False(body.GetProperty("canApprove").GetBoolean());
        JsonElement handover = body.GetProperty("handover");
        Assert.Equal(1, handover.GetProperty("turnCount").GetInt32());
        Assert.Equal(1, handover.GetProperty("invitationsSent").GetInt32() + handover.GetProperty("invitationsFailed").GetInt32());
        Assert.Equal("APPROVED", body.GetProperty("decisions")[0].GetProperty("kind").GetString());

        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.HandoverInProgress, plan.Status);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverTurn turn = await db.Set<HandoverTurn>().AsNoTracking()
                .Include(t => t.Items).SingleAsync(t => t.VaultId == submitted.Vault.VaultId);
            Assert.Equal(HandoverTurnStatus.Invited, turn.Status);
            Assert.NotEmpty(turn.Items);
            Assert.Equal(0, await db.Set<HandoverGrant>().CountAsync());
        });

        // Người nhận chưa có quyền tải gì: không có grant nào để dùng
        HttpResponseMessage download = await submitted.Executor.Client.GetAsync(
            $"/api/v1/handover/grants/{Guid.NewGuid()}/assets/{Guid.NewGuid()}/content");
        await ApiTestHelpers.AssertProblemAsync(download, HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");
    }

    [SqlFact]
    public async Task Death03_ApproveRecordsDecisionWithVersionAndAssignment()
    {
        // Chuẩn bị: Executor gửi tiếp bản 2 nên Verifier xét và duyệt bản 2
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.ResubmitAsync(submitted);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-80));

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, "  " + ApproveReason + "  ");

        // Kiểm tra: một dòng Approved gắn bản 2, người duyệt và phân công; phân công vẫn còn hiệu lực
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseDecision decision = await db.Set<DeathCaseDecision>().AsNoTracking().SingleAsync(d => d.CaseId == submitted.CaseId);
            Assert.Equal(DeathCaseDecisionKind.Approved, decision.Kind);
            Assert.Equal(2, decision.VersionNo);
            Assert.Equal(ApproveReason, decision.Reason);
            Assert.Null(decision.RequestedParts);
            Assert.Equal(submitted.Verifier.User.Id, decision.VerifierUserId);
            Assert.Equal(submitted.AssignmentId, decision.AssignmentId);
            DeathCaseAssignment assignment = await db.Set<DeathCaseAssignment>().AsNoTracking().SingleAsync(a => a.Id == submitted.AssignmentId);
            Assert.Null(assignment.EndedAt);
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.ApprovedForHandover, stored.Status);
            Assert.NotNull(stored.ClosedAt);
        });
    }

    [SqlFact]
    public async Task Death09_ApproveBeforeObjectionEndsIsBlocked()
    {
        // Chuẩn bị: Owner mới nhận thư cách đây 10 giờ
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-10));

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra
        await AssertBlockedAsync(response, "OBJECTION_PERIOD_NOT_OVER", new List<string> { "OBJECTION_PERIOD_NOT_OVER" });
        await AssertNothingChangedAsync(submitted);
    }

    [SqlFact]
    public async Task Ac63_Death08_ApproveWhenOwnerNotNotifiedIsBlocked()
    {
        // Chuẩn bị: thông báo vẫn Pending, đồng hồ 72 giờ chưa chạy
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra
        await AssertBlockedAsync(response, "OWNER_NOT_NOTIFIED", new List<string> { "OWNER_NOT_NOTIFIED" });
        await AssertNothingChangedAsync(submitted);
    }

    [SqlFact]
    public async Task Death10_ApproveWithUnreviewedFlagIsBlocked()
    {
        // Chuẩn bị: có cờ chưa xem xét
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        Guid flagId = await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);

        // Hành động
        HttpResponseMessage blocked = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra: bị chặn
        await AssertBlockedAsync(blocked, "FLAGS_NOT_REVIEWED", new List<string> { "FLAGS_NOT_REVIEWED" });
        await AssertNothingChangedAsync(submitted);

        // Xem xét cờ (etag đổi) rồi duyệt bằng etag mới thì được
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage reviewed = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, detail.GetProperty("etag").GetString(), "Đã đối chiếu.");
        JsonElement reviewedBody = await ApiTestHelpers.ReadJsonAsync(reviewed);
        HttpResponseMessage approved = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, reviewedBody.GetProperty("etag").GetString(), ApproveReason);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [SqlFact]
    public async Task Death04_ApproveWithNewerDraftIsBlocked()
    {
        // Chuẩn bị: Executor đang soạn bản nháp mới hơn bản đang xét
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        await DeathCaseReviewTestData.CreateDraftVersionAsync(submitted);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra
        await AssertBlockedAsync(response, "NEWER_VERSION_PENDING", new List<string> { "NEWER_VERSION_PENDING" });
        await AssertNothingChangedAsync(submitted);
    }

    [SqlFact]
    public async Task State_ApproveFromSupplementRequiredIsBlocked()
    {
        // Chuẩn bị: hồ sơ đang chờ Executor bổ sung
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(), "Thiếu trang", new[] { "FILES" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra
        await AssertBlockedAsync(response, "NOT_UNDER_REVIEW", new List<string> { "NOT_UNDER_REVIEW" });
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.SupplementRequired, stored.Status);
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(
                d => d.CaseId == submitted.CaseId && d.Kind == DeathCaseDecisionKind.Approved));
        });
    }

    [SqlFact]
    public async Task Ac71_TwoApprovesAtOnceGiveOneDecisionAndOneHandover()
    {
        // Chuẩn bị: hai client của cùng Verifier bấm duyệt đồng thời với cùng etag
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string etag = detail.GetProperty("etag").GetString()!;
        HttpClient first = submitted.Verifier.Client;
        HttpClient second = DeathCaseReviewTestData.CreateClientFor(factory, submitted.Verifier.User).Client;

        // Hành động
        Task<HttpResponseMessage> firstTask = DeathCaseReviewTestData.ApproveAsync(first, submitted.CaseId, etag, ApproveReason);
        Task<HttpResponseMessage> secondTask = DeathCaseReviewTestData.ApproveAsync(second, submitted.CaseId, etag, ApproveReason);
        HttpResponseMessage[] responses = await Task.WhenAll(firstTask, secondTask);

        // Kiểm tra: mỗi bên 200 hoặc 409 EDIT_CONFLICT; ít nhất một bên thành công
        int ok = 0;
        int withHandover = 0;
        foreach (HttpResponseMessage response in responses)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                ok++;
                JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
                Assert.Equal("APPROVED_FOR_HANDOVER", body.GetProperty("status").GetString());
                if (body.GetProperty("handover").ValueKind != JsonValueKind.Null)
                {
                    withHandover++;
                }
            }
            else
            {
                await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EDIT_CONFLICT");
            }
        }
        Assert.True(ok >= 1);
        Assert.Equal(1, withHandover);

        // Đúng một dòng Approved, số lượt = số chỉ định (1), kế hoạch ở HANDOVER_IN_PROGRESS
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseDecision>().CountAsync(
                d => d.CaseId == submitted.CaseId && d.Kind == DeathCaseDecisionKind.Approved));
            int designations = await db.Set<Designation>().CountAsync(d => d.Container.VaultId == submitted.Vault.VaultId);
            Assert.Equal(designations, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
            Assert.Equal(1, designations);
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.ApprovedForHandover, stored.Status);
        });
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.HandoverInProgress, plan.Status);
    }

    [SqlFact]
    public async Task Ac71_RepeatApproveReturnsSameResult()
    {
        // Chuẩn bị: đã duyệt một lần
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string oldEtag = detail.GetProperty("etag").GetString()!;
        HttpResponseMessage first = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, ApproveReason);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);
        Assert.NotEqual(JsonValueKind.Null, firstBody.GetProperty("handover").ValueKind);

        // Hành động: gọi lại với etag đã cũ và lý do khác
        HttpResponseMessage repeat = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, "Lý do khác");
        JsonElement repeatBody = await ApiTestHelpers.ReadJsonAsync(repeat);

        // Kiểm tra: 200 trạng thái hiện tại, handover null, vẫn một dòng quyết định và một lượt
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal("APPROVED_FOR_HANDOVER", repeatBody.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, repeatBody.GetProperty("handover").ValueKind);
        Assert.Equal(firstBody.GetProperty("etag").GetString(), repeatBody.GetProperty("etag").GetString());
        Assert.Equal(1, repeatBody.GetProperty("decisions").GetArrayLength());
        Assert.Equal(ApproveReason, repeatBody.GetProperty("decisions")[0].GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(1, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
    }

    [SqlFact]
    public async Task Ac71_RejectAfterApproveIsCaseClosed()
    {
        // Chuẩn bị: hồ sơ đã được duyệt
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approved = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);
        JsonElement approvedBody = await ApiTestHelpers.ReadJsonAsync(approved);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        // Hành động
        HttpResponseMessage reject = await DeathCaseReviewTestData.RejectAsync(
            submitted.Verifier.Client, submitted.CaseId, approvedBody.GetProperty("etag").GetString(), "Đổi ý");

        // Kiểm tra: 409 CASE_CLOSED, kế hoạch vẫn ở bàn giao
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(reject, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("CASE_CLOSED", problem.GetProperty("reason").GetString());
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.HandoverInProgress, plan.Status);
    }

    [SqlFact]
    public async Task Ac71_ApproveAfterRejectIsCaseClosed()
    {
        // Chuẩn bị: hồ sơ đã bị từ chối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, "Giấy tờ sai.");
        JsonElement rejectedBody = await ApiTestHelpers.ReadJsonAsync(rejected);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        // Hành động: duyệt (kể cả với etag mới nhất)
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, rejectedBody.GetProperty("etag").GetString(), ApproveReason);

        // Kiểm tra: 409 CASE_CLOSED, không thêm dòng, không có lượt bàn giao
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("CASE_CLOSED", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(0, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
    }

    [SqlFact]
    public async Task Death06_ApproveWithExistingTurnIsPlanStateInvalid()
    {
        // Chuẩn bị: đã có một lượt bàn giao nhưng kế hoạch vẫn DEATH_CASE_OPEN (dữ liệu lệch)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Designation designation = await db.Set<Designation>()
                .Include(d => d.Container)
                .SingleAsync(d => d.Container.VaultId == submitted.Vault.VaultId);
            HandoverTurn turn = new HandoverTurn();
            turn.VaultId = submitted.Vault.VaultId;
            turn.PlanId = submitted.Vault.PlanId;
            turn.ContainerId = designation.ContainerId;
            turn.ContainerName = designation.Container.Name;
            turn.DesignationId = designation.Id;
            turn.BeneficiaryId = designation.BeneficiaryId;
            turn.Status = HandoverTurnStatus.Invited;
            turn.InvitationTokenHash = Guid.NewGuid().ToString("N");
            turn.InvitedAt = DateTimeOffset.UtcNow;
            turn.ResponseDeadline = DateTimeOffset.UtcNow.AddDays(5);
            turn.CreatedAt = DateTimeOffset.UtcNow;
            db.Set<HandoverTurn>().Add(turn);
            await db.SaveChangesAsync();
        });

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra: 409 PLAN_STATE_INVALID, hồ sơ vẫn đang xét, không dòng Approved, chỉ còn lượt đã chèn
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("PLAN_STATE_INVALID", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.UnderReview, stored.Status);
            Assert.Null(stored.ClosedAt);
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(1, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
    }

    [SqlFact]
    public async Task Death06_ApproveWithoutReadyContainerKeepsCaseUnderReview()
    {
        // Chuẩn bị: gói của kho không còn READY (không có gì để bàn giao)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<Container>().Where(c => c.VaultId == submitted.Vault.VaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContainerStatus.Draft));
        });

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra: 409 HANDOVER_NOT_ALLOWED, hồ sơ vẫn đang xét, không dòng Approved, không lượt
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("NOTHING_TO_HAND_OVER", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.UnderReview, stored.Status);
            Assert.Null(stored.ClosedAt);
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(0, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
    }

    [SqlFact]
    public async Task Ac67_ApproveByVerifierWhoIsOwnerIsConflict()
    {
        // Chuẩn bị: hồ sơ được giao lại cho chính Owner của kho (giả lập nhánh C), Owner mang vai trò Verifier
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        Guid ownerId = submitted.Vault.Owner.Id;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == ownerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Verifier));
        });
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);
        await DeathCaseReviewTestData.AddAssignmentAsync(
            factory, submitted.CaseId, ownerId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(48));
        User owner = await LoadUserAsync(ownerId);
        DeathCaseReviewTestData.VerifierClient ownerAsVerifier = DeathCaseReviewTestData.CreateClientFor(factory, owner);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(ownerAsVerifier.Client, submitted.CaseId);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveAsync(
            ownerAsVerifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(), ApproveReason);

        // Kiểm tra: không có lý do chặn nào khác nên bị VERIFIER_CONFLICT; không ghi gì
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERIFIER_CONFLICT", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(0, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
    }

    [SqlFact]
    public async Task Ac67_DetailShowsVerifierConflictBlocker()
    {
        // Chuẩn bị: hồ sơ duyệt được về mọi mặt, nhưng Verifier được giao lại là Executor dự phòng của kho
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        JsonElement clean = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        Assert.True(clean.GetProperty("canApprove").GetBoolean());
        Assert.Empty(DeathCaseApi.ReadStrings(clean.GetProperty("approveBlockers")));
        await DeathCaseTestData.AddAcceptedExecutorAsync(
            factory, submitted.Vault.VaultId, submitted.Verifier.User.Id, ExecutorRole.Backup);

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra: VERIFIER_CONFLICT nằm CUỐI danh sách, không duyệt được
        Assert.False(detail.GetProperty("canApprove").GetBoolean());
        Assert.Equal(new List<string> { "VERIFIER_CONFLICT" }, DeathCaseApi.ReadStrings(detail.GetProperty("approveBlockers")));
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(), ApproveReason);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERIFIER_CONFLICT", problem.GetProperty("reason").GetString());

        // Khi còn lý do khác (Owner chưa nhận thư) thì VERIFIER_CONFLICT đứng sau chúng
        DeathCaseReviewTestData.SubmittedCase other = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseTestData.AddAcceptedExecutorAsync(
            factory, other.Vault.VaultId, other.Verifier.User.Id, ExecutorRole.Backup);
        JsonElement otherDetail = await DeathCaseReviewTestData.GetDetailAsync(other.Verifier.Client, other.CaseId);
        Assert.Equal(new List<string> { "OWNER_NOT_NOTIFIED", "VERIFIER_CONFLICT" },
            DeathCaseApi.ReadStrings(otherDetail.GetProperty("approveBlockers")));
    }

    private static async Task AssertBlockedAsync(HttpResponseMessage response, string reason, List<string> blockers)
    {
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal(reason, problem.GetProperty("reason").GetString());
        Assert.Equal(blockers, DeathCaseApi.ReadStrings(problem.GetProperty("approveBlockers")));
    }

    /// <summary>Hồ sơ vẫn đang xét, không có quyết định nào, không có lượt, kế hoạch vẫn DEATH_CASE_OPEN.</summary>
    private async Task AssertNothingChangedAsync(DeathCaseReviewTestData.SubmittedCase submitted)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.UnderReview, stored.Status);
            Assert.Null(stored.ClosedAt);
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(0, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
    }

    private async Task<User> LoadUserAsync(Guid userId)
    {
        User? user = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        });
        return user!;
    }
}
