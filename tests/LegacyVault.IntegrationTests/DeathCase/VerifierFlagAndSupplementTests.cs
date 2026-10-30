using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Verifier xem xét cờ và yêu cầu bổ sung, cùng phần Executor thấy yêu cầu và gửi lại
/// (DEATH-01, DEATH-10, STATE-03, ROLE-03, ASSIGN-04, AC-08, AC-09, AC-67).
/// </summary>
public sealed class VerifierFlagAndSupplementTests : IAsyncLifetime
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
    public async Task Death10_ReviewFlagUnblocksApprove()
    {
        // Chuẩn bị: Owner đã nhận thư và hết 72 giờ; còn một cờ chưa xem xét
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-80));
        Guid flagId = await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);
        JsonElement before = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        Assert.Equal(new List<string> { "FLAGS_NOT_REVIEWED" }, DeathCaseApi.ReadStrings(before.GetProperty("approveBlockers")));
        Assert.Equal(1, before.GetProperty("flags").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("flags")[0].GetProperty("reviewedAt").ValueKind);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, before.GetProperty("etag").GetString(), "Đã đối chiếu, hợp lệ.");
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra: hết chặn FLAGS_NOT_REVIEWED
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(DeathCaseApi.ReadStrings(after.GetProperty("approveBlockers")));
        Assert.True(after.GetProperty("canApprove").GetBoolean());
        JsonElement flag = after.GetProperty("flags")[0];
        Assert.NotEqual(JsonValueKind.Null, flag.GetProperty("reviewedAt").ValueKind);
        Assert.Equal("Đã đối chiếu, hợp lệ.", flag.GetProperty("reviewNote").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseFlag stored = await db.Set<DeathCaseFlag>().AsNoTracking().SingleAsync(f => f.Id == flagId);
            Assert.Equal(submitted.Verifier.User.Id, stored.ReviewedByUserId);
        });
    }

    [SqlFact]
    public async Task Death10_ReviewFlagTwiceKeepsFirstNote()
    {
        // Chuẩn bị: cờ đã được xem xét một lần
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid flagId = await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string oldEtag = detail.GetProperty("etag").GetString()!;
        HttpResponseMessage first = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, oldEtag, "Ghi chú đầu");
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);

        // Hành động: gọi lại với ghi chú khác và etag đã cũ
        HttpResponseMessage second = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, oldEtag, "Ghi chú sau");
        JsonElement secondBody = await ApiTestHelpers.ReadJsonAsync(second);

        // Kiểm tra: 200, giữ ghi chú đầu, etag không đổi
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Ghi chú đầu", secondBody.GetProperty("flags")[0].GetProperty("reviewNote").GetString());
        Assert.Equal(firstBody.GetProperty("etag").GetString(), secondBody.GetProperty("etag").GetString());
    }

    [SqlFact]
    public async Task Death10_FlagOfOlderVersionIsNotFound()
    {
        // Chuẩn bị: cờ gắn bản 1, nhưng Executor đã gửi bản 2 nên Verifier xét bản 2
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid oldFlagId = await DeathCaseReviewTestData.AddFlagToVersionAsync(factory, submitted.CaseId, 1);
        await DeathCaseReviewTestData.ResubmitAsync(submitted);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, oldFlagId, detail.GetProperty("etag").GetString(), "Ghi chú");
        HttpResponseMessage unknown = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, Guid.NewGuid(), detail.GetProperty("etag").GetString(), "Ghi chú");

        // Kiểm tra: cờ bản cũ và cờ lạ đều 404; cờ bản cũ không bị đụng tới
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        Assert.Equal(0, detail.GetProperty("flags").GetArrayLength());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseFlag stored = await db.Set<DeathCaseFlag>().AsNoTracking().SingleAsync(f => f.Id == oldFlagId);
            Assert.Null(stored.ReviewedAt);
        });
    }

    [SqlFact]
    public async Task Death10_ReviewFlagChangesEtag()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid flagId = await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);
        JsonElement before = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động: xem xét cờ rồi thử một thao tác ghi khác bằng etag cũ
        HttpResponseMessage reviewed = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, before.GetProperty("etag").GetString(), "Đã xem");
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(reviewed);
        HttpResponseMessage withOldEtag = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, before.GetProperty("etag").GetString(), "Thiếu trang", new[] { "FILES" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.NotEqual(before.GetProperty("etag").GetString(), after.GetProperty("etag").GetString());
        await ApiTestHelpers.AssertProblemAsync(withOldEtag, HttpStatusCode.Conflict, "EDIT_CONFLICT");
    }

    [SqlFact]
    public async Task Ac08_Death01_SupplementRequestMovesCaseAndKeepsAssignment()
    {
        // Chuẩn bị: Owner đã nhận thư (đồng hồ 72 giờ đang chạy)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset deliveredAt = DateTimeOffset.UtcNow.AddHours(-10);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, submitted.CaseId, deliveredAt);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        DeathCaseAssignment? assignmentBefore = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(assignmentBefore);

        // Hành động: requestedParts có trùng, thừa khoảng trắng, sai thứ tự
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "  Ảnh giấy chứng tử bị mờ  ", new[] { "FILES", " DATE_OF_DEATH ", "FILES" });
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SUPPLEMENT_REQUIRED", body.GetProperty("status").GetString());
        JsonElement decision = body.GetProperty("decisions")[0];
        Assert.Equal("SUPPLEMENT_REQUESTED", decision.GetProperty("kind").GetString());
        Assert.Equal(1, decision.GetProperty("versionNo").GetInt32());
        Assert.Equal("Ảnh giấy chứng tử bị mờ", decision.GetProperty("reason").GetString());
        Assert.Equal(new List<string> { "DATE_OF_DEATH", "FILES" }, DeathCaseApi.ReadStrings(decision.GetProperty("requestedParts")));
        Assert.False(body.GetProperty("canApprove").GetBoolean());
        Assert.Contains("NOT_UNDER_REVIEW", DeathCaseApi.ReadStrings(body.GetProperty("approveBlockers")));

        // Phân công, đồng hồ 72 giờ và kế hoạch giữ nguyên
        DeathCaseAssignment? assignmentAfter = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(assignmentAfter);
        Assert.Equal(assignmentBefore.Id, assignmentAfter.Id);
        Assert.Equal(assignmentBefore.DueAt, assignmentAfter.DueAt);
        Assert.Equal(assignmentBefore.AssignedAt, assignmentAfter.AssignedAt);
        Assert.Null(assignmentAfter.EndedAt);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase deathCase = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(deliveredAt.AddHours(72), deathCase.ObjectionEndsAt);
            Assert.Equal(1, deathCase.ReviewVersionNo);
            HandoverPlan plan = await db.HandoverPlans.AsNoTracking().SingleAsync(p => p.Id == deathCase.PlanId);
            Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
        });
    }

    [SqlFact]
    public async Task Ac08_ExecutorSeesSupplementRequestWithoutVerifierInfo()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        JsonElement beforeRequest = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);
        Assert.Equal(JsonValueKind.Null, beforeRequest.GetProperty("supplementRequest").ValueKind);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Cần ảnh rõ hơn", new[] { "FILES", "ISSUED_BY" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);

        // Hành động
        HttpResponseMessage response = await submitted.Executor.Client.GetAsync(DeathCaseApi.CaseUrl(submitted.CaseId));
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement body = JsonSerializer.Deserialize<JsonElement>(raw);

        // Kiểm tra: Executor thấy yêu cầu nhưng không thấy gì về Verifier
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SUPPLEMENT_REQUIRED", body.GetProperty("status").GetString());
        JsonElement request = body.GetProperty("supplementRequest");
        Assert.Equal(1, request.GetProperty("versionNo").GetInt32());
        Assert.Equal("Cần ảnh rõ hơn", request.GetProperty("reason").GetString());
        Assert.Equal(new List<string> { "FILES", "ISSUED_BY" }, DeathCaseApi.ReadStrings(request.GetProperty("requestedParts")));
        Assert.NotEqual(JsonValueKind.Null, request.GetProperty("requestedAt").ValueKind);
        Assert.DoesNotContain(submitted.Verifier.User.Id.ToString(), raw);
        Assert.DoesNotContain(submitted.Verifier.User.Email, raw);
        Assert.DoesNotContain("verifier", raw, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Death01_RepeatSameSupplementReturnsOkWithoutNewRow()
    {
        // Chuẩn bị: đã yêu cầu bổ sung một lần
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string oldEtag = detail.GetProperty("etag").GetString()!;
        HttpResponseMessage first = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, "Thiếu trang", new[] { "FILES" });
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);

        // Hành động: gọi lại y hệt (etag đã cũ), rồi gọi với nội dung khác và etag cũ
        HttpResponseMessage repeat = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, "Thiếu trang", new[] { "FILES" });
        JsonElement repeatBody = await ApiTestHelpers.ReadJsonAsync(repeat);
        HttpResponseMessage different = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, oldEtag, "Thiếu trang khác", new[] { "FILES" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(1, repeatBody.GetProperty("decisions").GetArrayLength());
        Assert.Equal(firstBody.GetProperty("etag").GetString(), repeatBody.GetProperty("etag").GetString());
        await ApiTestHelpers.AssertProblemAsync(different, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death01_SupplementWithUnknownPartIsRejected()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string etag = detail.GetProperty("etag").GetString()!;

        // Hành động
        HttpResponseMessage unknown = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, "Lý do", new[] { "PASSPORT" });
        HttpResponseMessage lowercase = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, "Lý do", new[] { "files" });
        HttpResponseMessage empty = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, "Lý do", new string[0]);
        HttpResponseMessage noReason = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, "   ", new[] { "FILES" });

        // Kiểm tra: 400 theo đúng trường; không ghi gì
        JsonElement unknownProblem = await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(unknownProblem.GetProperty("errors").TryGetProperty("requestedParts", out JsonElement _));
        await ApiTestHelpers.AssertProblemAsync(lowercase, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(empty, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        JsonElement noReasonProblem = await ApiTestHelpers.AssertProblemAsync(noReason, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(noReasonProblem.GetProperty("errors").TryGetProperty("reason", out JsonElement _));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death01_SupplementAllowedWhileExecutorHasDraft()
    {
        // Chuẩn bị: Executor đang soạn bản nháp số 2
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.CreateDraftVersionAsync(submitted);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        Assert.Contains("NEWER_VERSION_PENDING", DeathCaseApi.ReadStrings(detail.GetProperty("approveBlockers")));

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Cần bổ sung ngày mất", new[] { "DATE_OF_DEATH" });
        JsonElement executorView = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);

        // Kiểm tra: vẫn yêu cầu được; Executor vẫn có bản nháp và thấy yêu cầu
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SUPPLEMENT_REQUIRED", executorView.GetProperty("status").GetString());
        Assert.True(executorView.GetProperty("currentVersion").GetProperty("isDraft").GetBoolean());
        Assert.Equal(2, executorView.GetProperty("currentVersion").GetProperty("versionNo").GetInt32());
        Assert.Equal(1, executorView.GetProperty("supplementRequest").GetProperty("versionNo").GetInt32());
    }

    [SqlFact]
    public async Task Ac67_Role03_VerifierWhoBecameExecutorGetsConflict()
    {
        // Chuẩn bị: sau khi được giao, Verifier trở thành Executor của chính kho này
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid flagId = await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);
        await DeathCaseTestData.AddAcceptedExecutorAsync(
            factory, submitted.Vault.VaultId, submitted.Verifier.User.Id);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string etag = detail.GetProperty("etag").GetString()!;

        // Hành động
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, "Thiếu trang", new[] { "FILES" });
        HttpResponseMessage review = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, etag, "Đã xem");

        // Kiểm tra: xem thì vẫn cho (GetDetailAsync ở trên đã 200), ghi thì bị chặn; không ghi gì
        JsonElement supplementProblem = await ApiTestHelpers.AssertProblemAsync(supplement, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERIFIER_CONFLICT", supplementProblem.GetProperty("reason").GetString());
        JsonElement reviewProblem = await ApiTestHelpers.AssertProblemAsync(review, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERIFIER_CONFLICT", reviewProblem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Null((await db.Set<DeathCaseFlag>().AsNoTracking().SingleAsync(f => f.Id == flagId)).ReviewedAt);
        });
    }

    [SqlFact]
    public async Task State03_StaleEtagAfterExecutorNewVersionIsRejected()
    {
        // Chuẩn bị: Verifier đọc hồ sơ, sau đó Executor tạo bản nháp (UpdatedAt đổi)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        await DeathCaseReviewTestData.CreateDraftVersionAsync(submitted);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Thiếu trang", new[] { "FILES" });

        // Kiểm tra: etag cũ bị từ chối, không ghi gì
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Ac09_ResubmitClearsSupplementRequestAndReturnsUnderReview()
    {
        // Chuẩn bị: Verifier đã yêu cầu bổ sung
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Thiếu nơi cấp", new[] { "ISSUED_BY" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);

        // Hành động: Executor tạo bản mới từ hồ sơ SUPPLEMENT_REQUIRED rồi gửi lại
        JsonElement resubmitted = await DeathCaseReviewTestData.ResubmitAsync(submitted);
        JsonElement verifierView = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra: về UNDER_REVIEW, hết yêu cầu bổ sung; Verifier xét bản 2 và vẫn thấy lịch sử quyết định
        Assert.Equal("UNDER_REVIEW", resubmitted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, resubmitted.GetProperty("supplementRequest").ValueKind);
        Assert.Equal(2, resubmitted.GetProperty("reviewVersionNo").GetInt32());
        Assert.Equal("UNDER_REVIEW", verifierView.GetProperty("status").GetString());
        Assert.Equal(2, verifierView.GetProperty("reviewVersion").GetProperty("versionNo").GetInt32());
        Assert.Equal(1, verifierView.GetProperty("decisions").GetArrayLength());
        Assert.Equal(1, verifierView.GetProperty("decisions")[0].GetProperty("versionNo").GetInt32());
    }

    [SqlFact]
    public async Task Assign04_ResubmitAfterSupplementResetsDueAt()
    {
        // Chuẩn bị: sau yêu cầu bổ sung, hạn xét gần hết (còn 1 giờ)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Thiếu nơi cấp", new[] { "ISSUED_BY" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);
        DateTimeOffset almostDue = DateTimeOffset.UtcNow.AddHours(1);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, almostDue);
        DeathCaseAssignment? before = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(before);

        // Hành động
        DateTimeOffset beforeSubmit = DateTimeOffset.UtcNow;
        await DeathCaseReviewTestData.ResubmitAsync(submitted);
        DateTimeOffset afterSubmit = DateTimeOffset.UtcNow;

        // Kiểm tra: DueAt = giờ gửi + 48 giờ, AssignedAt và dòng phân công giữ nguyên
        DeathCaseAssignment? after = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(after);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.AssignedAt, after.AssignedAt);
        Assert.True(after.DueAt >= beforeSubmit.AddHours(48));
        Assert.True(after.DueAt <= afterSubmit.AddHours(48));
    }

    [SqlFact]
    public async Task Assign04_EndedAssignmentIsNotTouchedOnResubmit()
    {
        // Chuẩn bị: phân công đầu đã kết thúc (giao lại), có dòng phân công mới cho Verifier khác
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseAssignment? oldRow = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(oldRow);
        DeathCaseReviewTestData.VerifierClient newVerifier = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);
        DateTimeOffset newDue = DateTimeOffset.UtcNow.AddHours(5);
        DeathCaseAssignment newRow = await DeathCaseReviewTestData.AddAssignmentAsync(
            factory, submitted.CaseId, newVerifier.User.Id, DateTimeOffset.UtcNow, newDue);

        // Hành động
        DateTimeOffset beforeSubmit = DateTimeOffset.UtcNow;
        await DeathCaseReviewTestData.ResubmitAsync(submitted);

        // Kiểm tra: dòng đã kết thúc giữ nguyên DueAt; dòng đang hiệu lực được đặt lại
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseAssignment ended = await db.Set<DeathCaseAssignment>().AsNoTracking().SingleAsync(a => a.Id == oldRow.Id);
            Assert.Equal(oldRow.DueAt, ended.DueAt);
            Assert.NotNull(ended.EndedAt);
            DeathCaseAssignment active = await db.Set<DeathCaseAssignment>().AsNoTracking().SingleAsync(a => a.Id == newRow.Id);
            Assert.Null(active.EndedAt);
            Assert.True(active.DueAt >= beforeSubmit.AddHours(48));
            Assert.Equal(2, await db.Set<DeathCaseAssignment>().CountAsync(a => a.CaseId == submitted.CaseId));
        });
    }
}
