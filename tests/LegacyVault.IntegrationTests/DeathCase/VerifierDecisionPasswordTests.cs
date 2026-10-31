using System.Net;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// DEATH-03: bấm Duyệt/Từ chối là thao tác có đăng nhập, nhập lại mật khẩu xác nhận và ghi nhật ký (giao diện không gọi đó là chữ ký).
/// Nhập sai thì khóa lũy tiến 15 phút → 1 giờ → 24 giờ, trên SQL Server thật.
/// </summary>
public sealed class VerifierDecisionPasswordTests : IAsyncLifetime
{
    private const string ApproveReason = "Giấy chứng tử hợp lệ, thông tin khớp.";
    private const string RejectReason = "Giấy chứng tử không đọc được số hiệu.";
    private const string WrongPassword = "Sai-mat-khau-9";

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
    public async Task Death03_ApproveWithCorrectPasswordSavesDecision()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra: lưu đúng một quyết định duyệt, hồ sơ chuyển sang chờ bàn giao
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DeathCaseDecision decision = await LoadFinalDecisionAsync(submitted.CaseId);
        Assert.Equal(DeathCaseDecisionKind.Approved, decision.Kind);
        Assert.Equal(submitted.Verifier.User.Id, decision.VerifierUserId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.ApprovedForHandover, stored.Status);
        });
    }

    [SqlFact]
    public async Task Death03_RejectWithCorrectPasswordSavesDecision()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);

        // Kiểm tra: lưu đúng một quyết định từ chối, hồ sơ đã bị từ chối
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DeathCaseDecision decision = await LoadFinalDecisionAsync(submitted.CaseId);
        Assert.Equal(DeathCaseDecisionKind.Rejected, decision.Kind);
        Assert.Equal(RejectReason, decision.Reason);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.Rejected, stored.Status);
        });
    }

    [SqlFact]
    public async Task Death03_SupplementRequestNeedsNoPassword()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động: yêu cầu bổ sung không gửi mật khẩu
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Cần bản chụp rõ hơn.", new[] { "FILES" });

        // Kiểm tra: lưu yêu cầu bổ sung, không đụng bộ đếm sai mật khẩu
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseDecision decision = await db.Set<DeathCaseDecision>().AsNoTracking().SingleAsync(d => d.CaseId == submitted.CaseId);
            Assert.Equal(DeathCaseDecisionKind.SupplementRequested, decision.Kind);
        });
        Assert.Null(await LoadGuardAsync(submitted.Verifier.User.Id));
    }

    [SqlFact]
    public async Task Death03_ResponseHasNoSignatureFields()
    {
        // Chuẩn bị: hồ sơ đã duyệt
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage signature = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId) + "/decision-signature");

        // Kiểm tra: không còn trường trạng thái chữ ký, API xem chữ ký không còn
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.False(detail.TryGetProperty("decisionSignatureStatus", out JsonElement _));
        Assert.DoesNotContain("signature", detail.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, signature.StatusCode);
    }

    [SqlFact]
    public async Task Death03_WrongPasswordIsForbiddenAndNothingSaved()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await ApproveWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);

        // Kiểm tra: 403 (không phải 401), không có quyết định, hồ sơ vẫn đang xét
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.Equal(4, problem.GetProperty("attemptsRemaining").GetInt32());
        Assert.Equal("Mật khẩu xác nhận chưa đúng.", problem.GetProperty("title").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.UnderReview, stored.Status);
        });
    }

    [SqlFact]
    public async Task Death03_MissingPasswordIsValidationError()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage approve = await ApproveWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, null);
        HttpResponseMessage reject = await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, null);

        // Kiểm tra: 400 kèm câu báo của trường password, không tính là một lần đoán
        JsonElement approveProblem = await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        JsonElement rejectProblem = await ApiTestHelpers.AssertProblemAsync(reject, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Contains("Vui lòng nhập mật khẩu để xác nhận.", approveProblem.GetProperty("errors").ToString());
        Assert.Contains("Vui lòng nhập mật khẩu để xác nhận.", rejectProblem.GetProperty("errors").ToString());
        Assert.Null(await LoadGuardAsync(submitted.Verifier.User.Id));
    }

    [SqlFact]
    public async Task Death03_VerifierWithoutPasswordCannotDecide()
    {
        // Chuẩn bị: Verifier chỉ đăng nhập Google (không có mật khẩu)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid verifierId = submitted.Verifier.User.Id;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == verifierId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, (string?)null));
        });

        // Hành động
        HttpResponseMessage response = await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);

        // Kiểm tra: 403 có câu báo riêng, không tính lần đoán, không có quyết định
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.False(problem.TryGetProperty("attemptsRemaining", out JsonElement _));
        Assert.Equal("Tài khoản chưa có mật khẩu nên chưa xác nhận được. Vui lòng đặt mật khẩu trước.",
            problem.GetProperty("title").GetString());
        Assert.Null(await LoadGuardAsync(verifierId));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death03_ReassignedVerifierCannotDecideOldCase()
    {
        // Chuẩn bị: phân công của Verifier đã bị kết thúc (job giao lại)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string? etag = detail.GetProperty("etag").GetString();
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);

        // Hành động: thử cả mật khẩu sai lẫn đúng (etag lấy trước khi bị giao lại)
        HttpResponseMessage wrong = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, ApproveReason, WrongPassword);
        HttpResponseMessage correct = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, ApproveReason, DeathCaseTestData.TestPassword);

        // Kiểm tra: 404 trước khi kiểm mật khẩu, không tạo dòng bộ đếm (không tốn lần đoán), không có quyết định mới
        await ApiTestHelpers.AssertProblemAsync(wrong, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(correct, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        Assert.Null(await LoadGuardAsync(submitted.Verifier.User.Id));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death03_FifthWrongPasswordLocksDecisionFifteenMinutes()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        List<int> remaining = new List<int>();

        // Hành động: sai 4 lần rồi lần thứ 5
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            HttpResponseMessage wrong = await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);
            JsonElement wrongProblem = await ApiTestHelpers.AssertProblemAsync(
                wrong, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
            remaining.Add(wrongProblem.GetProperty("attemptsRemaining").GetInt32());
        }
        DateTimeOffset before = DateTimeOffset.UtcNow;
        HttpResponseMessage fifth = await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);

        // Kiểm tra: 4 → 1 lần còn lại, lần 5 bị khóa 15 phút
        Assert.Equal(new List<int> { 4, 3, 2, 1 }, remaining);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(fifth, HttpStatusCode.Locked, "SIGNING_LOCKED");
        AssertLockedFor(problem, before, TimeSpan.FromMinutes(15));
        Assert.Equal("Chức năng duyệt/từ chối đang tạm khóa vì nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau.",
            problem.GetProperty("title").GetString());
        VerifierSigningGuard? guard = await LoadGuardAsync(submitted.Verifier.User.Id);
        Assert.NotNull(guard);
        Assert.Equal(0, guard.FailedCount);
        Assert.Equal(1, guard.LockLevel);
    }

    [SqlFact]
    public async Task Death03_CorrectPasswordWhileLockedIsStillLocked()
    {
        // Chuẩn bị: đã bị khóa duyệt/từ chối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await LockDecisionAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động
        HttpResponseMessage response = await RejectWithPasswordAsync(
            submitted.Verifier.Client, submitted.CaseId, DeathCaseTestData.TestPassword);

        // Kiểm tra: vẫn 423, không có quyết định
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Locked, "SIGNING_LOCKED");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death03_SecondLockLastsOneHourThirdLastsOneDay()
    {
        // Chuẩn bị: bị khóa lần 1, rồi cho hết hạn khóa
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid verifierId = submitted.Verifier.User.Id;
        await LockDecisionAsync(submitted.Verifier.Client, submitted.CaseId);
        await ExpireLockAsync(verifierId);

        // Hành động: sai đủ 5 lần nữa (khóa lần 2), hết hạn, rồi sai 5 lần nữa (khóa lần 3)
        DateTimeOffset beforeSecond = DateTimeOffset.UtcNow;
        JsonElement secondLock = await LockDecisionAsync(submitted.Verifier.Client, submitted.CaseId);
        await ExpireLockAsync(verifierId);
        DateTimeOffset beforeThird = DateTimeOffset.UtcNow;
        JsonElement thirdLock = await LockDecisionAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        AssertLockedFor(secondLock, beforeSecond, TimeSpan.FromHours(1));
        AssertLockedFor(thirdLock, beforeThird, TimeSpan.FromHours(24));
        VerifierSigningGuard? guard = await LoadGuardAsync(verifierId);
        Assert.NotNull(guard);
        Assert.Equal(3, guard.LockLevel);
    }

    [SqlFact]
    public async Task Death03_CorrectPasswordResetsCounter()
    {
        // Chuẩn bị: sai 3 lần, bậc khóa đang là 2 (đã từng bị khóa, nay đã hết hạn)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid verifierId = submitted.Verifier.User.Id;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);
        }
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<VerifierSigningGuard>().Where(g => g.UserId == verifierId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.LockLevel, 2));
        });

        // Hành động
        HttpResponseMessage response = await RejectWithPasswordAsync(
            submitted.Verifier.Client, submitted.CaseId, DeathCaseTestData.TestPassword);

        // Kiểm tra: từ chối thành công, bộ đếm và bậc khóa về 0 (lưu cùng quyết định)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        VerifierSigningGuard? guard = await LoadGuardAsync(verifierId);
        Assert.NotNull(guard);
        Assert.Equal(0, guard.FailedCount);
        Assert.Equal(0, guard.LockLevel);
        Assert.Null(guard.LockedUntil);
    }

    [SqlFact]
    public async Task Death03_WrongPasswordCountIsSavedEvenThoughRequestFails()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động: hai request sai, mỗi request đều trả lỗi
        HttpResponseMessage first = await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);
        HttpResponseMessage second = await ApproveWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);

        // Kiểm tra: bộ đếm trong DB đã lưu cả 2 lần sai (tính chung cho Duyệt và Từ chối)
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(second, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.Equal(3, problem.GetProperty("attemptsRemaining").GetInt32());
        VerifierSigningGuard? guard = await LoadGuardAsync(submitted.Verifier.User.Id);
        Assert.NotNull(guard);
        Assert.Equal(2, guard.FailedCount);
        Assert.Null(guard.LockedUntil);
    }

    [SqlFact]
    public async Task Death03_LockOnlyBlocksDecisionNotReading()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await LockDecisionAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động
        HttpResponseMessage detail = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));
        HttpResponseMessage list = await submitted.Verifier.Client.GetAsync("/api/v1/verifier/death-cases");

        // Kiểm tra: vẫn xem được hồ sơ khi bị khóa duyệt/từ chối
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [SqlFact]
    public async Task Death03_LockIsPerVerifierNotPerCase()
    {
        // Chuẩn bị: cùng Verifier được giao hồ sơ A và B, bị khóa ở hồ sơ A
        DeathCaseReviewTestData.SubmittedCase first = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid secondCaseId = await CreateCaseAssignedToAsync(first.Verifier.User.Id);
        await LockDecisionAsync(first.Verifier.Client, first.CaseId);

        // Hành động: từ chối ở hồ sơ B với mật khẩu đúng
        HttpResponseMessage response = await RejectWithPasswordAsync(
            first.Verifier.Client, secondCaseId, DeathCaseTestData.TestPassword);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Locked, "SIGNING_LOCKED");
    }

    /// <summary>Đọc etag hiện tại rồi duyệt với mật khẩu cho trước (null = không gửi trường password).</summary>
    private static async Task<HttpResponseMessage> ApproveWithPasswordAsync(HttpClient client, Guid caseId, string? password)
    {
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(client, caseId);
        return await DeathCaseReviewTestData.ApproveAsync(
            client, caseId, detail.GetProperty("etag").GetString(), ApproveReason, password);
    }

    /// <summary>Đọc etag hiện tại rồi từ chối với mật khẩu cho trước (null = không gửi trường password).</summary>
    private static async Task<HttpResponseMessage> RejectWithPasswordAsync(HttpClient client, Guid caseId, string? password)
    {
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(client, caseId);
        return await DeathCaseReviewTestData.RejectAsync(
            client, caseId, detail.GetProperty("etag").GetString(), RejectReason, password);
    }

    /// <summary>Nhập sai đủ 5 lần để bị khóa duyệt/từ chối; trả ProblemDetails 423 của lần thứ 5.</summary>
    private static async Task<JsonElement> LockDecisionAsync(HttpClient client, Guid caseId)
    {
        for (int attempt = 1; attempt < VaultPinRules.MaxFailedAttempts; attempt++)
        {
            HttpResponseMessage wrong = await RejectWithPasswordAsync(client, caseId, WrongPassword);
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }
        HttpResponseMessage last = await RejectWithPasswordAsync(client, caseId, WrongPassword);
        return await ApiTestHelpers.AssertProblemAsync(last, HttpStatusCode.Locked, "SIGNING_LOCKED");
    }

    private static void AssertLockedFor(JsonElement problem, DateTimeOffset before, TimeSpan duration)
    {
        DateTimeOffset lockedUntil = problem.GetProperty("lockedUntil").GetDateTimeOffset();
        Assert.InRange(lockedUntil, before + duration - TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow + duration + TimeSpan.FromMinutes(1));
    }

    /// <summary>Giả lập hết hạn khóa: lùi LockedUntil về quá khứ (giữ nguyên bậc khóa).</summary>
    private async Task ExpireLockAsync(Guid verifierId)
    {
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-1);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<VerifierSigningGuard>().Where(g => g.UserId == verifierId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.LockedUntil, past));
        });
    }

    /// <summary>Tạo hồ sơ thứ hai rồi giao lại cho Verifier cho trước (kết thúc phân công của Verifier được giao tự động).</summary>
    private async Task<Guid> CreateCaseAssignedToAsync(Guid verifierId)
    {
        DeathCaseReviewTestData.SubmittedCase second = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, second.CaseId, now);
        await DeathCaseReviewTestData.AddAssignmentAsync(factory, second.CaseId, verifierId, now, now.AddHours(72));
        return second.CaseId;
    }

    private async Task<VerifierSigningGuard?> LoadGuardAsync(Guid verifierId)
    {
        VerifierSigningGuard? guard = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            guard = await db.Set<VerifierSigningGuard>().AsNoTracking().SingleOrDefaultAsync(g => g.UserId == verifierId);
        });
        return guard;
    }

    private async Task<DeathCaseDecision> LoadFinalDecisionAsync(Guid caseId)
    {
        DeathCaseDecision? decision = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            decision = await db.Set<DeathCaseDecision>().AsNoTracking()
                .SingleAsync(d => d.CaseId == caseId && d.Kind != DeathCaseDecisionKind.SupplementRequested);
        });
        return decision!;
    }
}
