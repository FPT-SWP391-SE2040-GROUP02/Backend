using System.Net;
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
/// DEATH-14: Verifier nhập lại mật khẩu và ký số quyết định khi Duyệt/Từ chối (RSA-2048, SHA-256, PSS),
/// khóa lũy tiến khi nhập sai (D11), trên SQL Server thật.
/// </summary>
public sealed class VerifierDecisionSignatureTests : IAsyncLifetime
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
    public async Task Death14_ApproveCreatesKeyAndSignsDecision()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra: một khóa của Verifier, quyết định có chữ ký 256 byte kiểm lại được bằng chuỗi dựng từ DB
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<VerifierSigningKey> keys = await LoadKeysAsync(submitted.Verifier.User.Id);
        VerifierSigningKey key = Assert.Single(keys);
        Assert.Equal(DecisionSignatureRules.Algorithm, key.Algorithm);
        Assert.Null(key.RevokedAt);
        DeathCaseDecision decision = await LoadFinalDecisionAsync(submitted.CaseId);
        Assert.Equal(DeathCaseDecisionKind.Approved, decision.Kind);
        Assert.Equal(key.Id, decision.SigningKeyId);
        Assert.NotNull(decision.Signature);
        Assert.Equal(256, decision.Signature.Length);
        Assert.True(await VerifyFromDatabaseAsync(decision, key));
    }

    [SqlFact]
    public async Task Death14_RejectSignsDecision()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        VerifierSigningKey key = Assert.Single(await LoadKeysAsync(submitted.Verifier.User.Id));
        DeathCaseDecision decision = await LoadFinalDecisionAsync(submitted.CaseId);
        Assert.Equal(DeathCaseDecisionKind.Rejected, decision.Kind);
        Assert.Equal(key.Id, decision.SigningKeyId);
        Assert.True(await VerifyFromDatabaseAsync(decision, key));
    }

    [SqlFact]
    public async Task Death14_SecondDecisionReusesSameKey()
    {
        // Chuẩn bị: cùng một Verifier được giao hai hồ sơ
        DeathCaseReviewTestData.SubmittedCase first = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid secondCaseId = await CreateCaseAssignedToAsync(first.Verifier.User.Id);
        HttpResponseMessage firstReject = await DeathCaseReviewTestData.RejectNowAsync(first, RejectReason);

        // Hành động
        HttpResponseMessage secondReject = await RejectWithPasswordAsync(
            first.Verifier.Client, secondCaseId, DeathCaseTestData.TestPassword);

        // Kiểm tra: chỉ một khóa, hai quyết định cùng ký bằng khóa đó
        Assert.Equal(HttpStatusCode.OK, firstReject.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondReject.StatusCode);
        VerifierSigningKey key = Assert.Single(await LoadKeysAsync(first.Verifier.User.Id));
        Assert.Equal(key.Id, (await LoadFinalDecisionAsync(first.CaseId)).SigningKeyId);
        Assert.Equal(key.Id, (await LoadFinalDecisionAsync(secondCaseId)).SigningKeyId);
    }

    [SqlFact]
    public async Task Death14_WrongPasswordIsForbiddenAndNothingSaved()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await ApproveWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, WrongPassword);

        // Kiểm tra: 403 (không phải 401), không có quyết định, không có khóa, hồ sơ vẫn đang xét
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.Equal(4, problem.GetProperty("attemptsRemaining").GetInt32());
        Assert.Empty(await LoadKeysAsync(submitted.Verifier.User.Id));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            DeathCase stored = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.UnderReview, stored.Status);
        });
    }

    [SqlFact]
    public async Task Death14_MissingPasswordIsValidationError()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage approve = await ApproveWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, null);
        HttpResponseMessage reject = await RejectWithPasswordAsync(submitted.Verifier.Client, submitted.CaseId, null);

        // Kiểm tra: 400, không tính là một lần đoán
        await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(reject, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Null(await LoadGuardAsync(submitted.Verifier.User.Id));
    }

    [SqlFact]
    public async Task Death14_VerifierWithoutPasswordCannotSign()
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
        Assert.Null(await LoadGuardAsync(verifierId));
        Assert.Empty(await LoadKeysAsync(verifierId));
    }

    [SqlFact]
    public async Task Death14_SupplementRequestIsNotSigned()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động: yêu cầu bổ sung không cần mật khẩu
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Cần bản chụp rõ hơn.", new[] { "FILES" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseDecision decision = await db.Set<DeathCaseDecision>().AsNoTracking().SingleAsync(d => d.CaseId == submitted.CaseId);
            Assert.Equal(DeathCaseDecisionKind.SupplementRequested, decision.Kind);
            Assert.Null(decision.Signature);
            Assert.Null(decision.SigningKeyId);
        });
        Assert.Empty(await LoadKeysAsync(submitted.Verifier.User.Id));
    }

    [SqlFact]
    public async Task Death14_ApproveBlockedByFlagSavesNoKey()
    {
        // Chuẩn bị: còn cờ chưa xem xét
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);

        // Kiểm tra: 409 như cũ, không có khóa mới nào được lưu
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Empty(await LoadKeysAsync(submitted.Verifier.User.Id));
    }

    [SqlFact]
    public async Task Death14_ReassignedVerifierCannotSignOldCase()
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

        // Kiểm tra: 404 trước khi kiểm mật khẩu, không tốn lần đoán, không có khóa, không có chữ ký
        await ApiTestHelpers.AssertProblemAsync(wrong, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(correct, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        Assert.Null(await LoadGuardAsync(submitted.Verifier.User.Id));
        Assert.Empty(await LoadKeysAsync(submitted.Verifier.User.Id));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Death14_FifthWrongPasswordLocksSigningFifteenMinutes()
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
        VerifierSigningGuard? guard = await LoadGuardAsync(submitted.Verifier.User.Id);
        Assert.NotNull(guard);
        Assert.Equal(0, guard.FailedCount);
        Assert.Equal(1, guard.LockLevel);
    }

    [SqlFact]
    public async Task Death14_CorrectPasswordWhileLockedIsStillLocked()
    {
        // Chuẩn bị: đã bị khóa ký
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await LockSigningAsync(submitted.Verifier.Client, submitted.CaseId);

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
    public async Task Death14_SecondLockLastsOneHourThirdLastsOneDay()
    {
        // Chuẩn bị: bị khóa lần 1, rồi cho hết hạn khóa
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid verifierId = submitted.Verifier.User.Id;
        await LockSigningAsync(submitted.Verifier.Client, submitted.CaseId);
        await ExpireLockAsync(verifierId);

        // Hành động: sai đủ 5 lần nữa (khóa lần 2), hết hạn, rồi sai 5 lần nữa (khóa lần 3)
        DateTimeOffset beforeSecond = DateTimeOffset.UtcNow;
        JsonElement secondLock = await LockSigningAsync(submitted.Verifier.Client, submitted.CaseId);
        await ExpireLockAsync(verifierId);
        DateTimeOffset beforeThird = DateTimeOffset.UtcNow;
        JsonElement thirdLock = await LockSigningAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        AssertLockedFor(secondLock, beforeSecond, TimeSpan.FromHours(1));
        AssertLockedFor(thirdLock, beforeThird, TimeSpan.FromHours(24));
        VerifierSigningGuard? guard = await LoadGuardAsync(verifierId);
        Assert.NotNull(guard);
        Assert.Equal(3, guard.LockLevel);
    }

    [SqlFact]
    public async Task Death14_CorrectPasswordResetsCounter()
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

        // Kiểm tra: ký thành công, bộ đếm và bậc khóa về 0 (lưu cùng quyết định)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        VerifierSigningGuard? guard = await LoadGuardAsync(verifierId);
        Assert.NotNull(guard);
        Assert.Equal(0, guard.FailedCount);
        Assert.Equal(0, guard.LockLevel);
        Assert.Null(guard.LockedUntil);
    }

    [SqlFact]
    public async Task Death14_WrongPasswordCountIsSavedEvenThoughRequestFails()
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
    public async Task Death14_LockOnlyBlocksSigningNotReading()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await LockSigningAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động
        HttpResponseMessage detail = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));
        HttpResponseMessage list = await submitted.Verifier.Client.GetAsync("/api/v1/verifier/death-cases");

        // Kiểm tra: vẫn xem được hồ sơ khi bị khóa ký
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [SqlFact]
    public async Task Death14_LockIsPerVerifierNotPerCase()
    {
        // Chuẩn bị: cùng Verifier được giao hồ sơ A và B, bị khóa ký ở hồ sơ A
        DeathCaseReviewTestData.SubmittedCase first = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid secondCaseId = await CreateCaseAssignedToAsync(first.Verifier.User.Id);
        await LockSigningAsync(first.Verifier.Client, first.CaseId);

        // Hành động: ký ở hồ sơ B với mật khẩu đúng
        HttpResponseMessage response = await RejectWithPasswordAsync(
            first.Verifier.Client, secondCaseId, DeathCaseTestData.TestPassword);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Locked, "SIGNING_LOCKED");
    }

    [SqlFact]
    public async Task Death14_SignatureOfApprovedCaseIsValid()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(SignatureUrl(submitted.CaseId));

        // Kiểm tra: phiếu đầy đủ, dấu vân tay và chữ ký khớp dữ liệu trong DB
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        VerifierSigningKey key = Assert.Single(await LoadKeysAsync(submitted.Verifier.User.Id));
        DeathCaseDecision decision = await LoadFinalDecisionAsync(submitted.CaseId);
        Assert.Equal("VALID", body.GetProperty("status").GetString());
        Assert.Equal("APPROVED", body.GetProperty("decisionKind").GetString());
        Assert.Equal(decision.VersionNo, body.GetProperty("versionNo").GetInt32());
        Assert.Equal(decision.DecidedAt, body.GetProperty("decidedAt").GetDateTimeOffset());
        Assert.Equal(submitted.Verifier.User.Id, body.GetProperty("signedByUserId").GetGuid());
        Assert.Equal(submitted.Verifier.User.FullName, body.GetProperty("signedByFullName").GetString());
        Assert.Equal("RSA-2048-PSS-SHA256", body.GetProperty("algorithm").GetString());
        Assert.Equal(DecisionSignatureRules.Fingerprint(key.PublicKey), body.GetProperty("keyFingerprint").GetString());
        Assert.Equal(Convert.ToBase64String(decision.Signature!), body.GetProperty("signatureBase64").GetString());
    }

    [SqlFact]
    public async Task Death14_TamperedReasonMakesSignatureInvalid()
    {
        // Chuẩn bị: đã từ chối có chữ ký, rồi có người sửa lý do thẳng trong DB
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage reject = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseDecision>().Where(d => d.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Reason, "Lý do đã bị sửa."));
        });

        // Hành động
        JsonElement body = await GetSignatureAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal("INVALID", body.GetProperty("status").GetString());
        Assert.Equal("REJECTED", body.GetProperty("decisionKind").GetString());
    }

    [SqlFact]
    public async Task Death14_TamperedDecidedAtMakesSignatureInvalid()
    {
        // Chuẩn bị: lùi thời điểm quyết định một giây
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage reject = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseDecision>().Where(d => d.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DecidedAt, d => d.DecidedAt.AddSeconds(-1)));
        });

        // Hành động
        JsonElement body = await GetSignatureAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal("INVALID", body.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Death14_SwappedPublicKeyInDatabaseIsInvalid()
    {
        // Chuẩn bị: kẻ sửa DB tự sinh cặp khóa khác, ký lại chuỗi đúng rồi thay PublicKey + Signature trong DB
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage reject = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        VerifierSigningKey key = Assert.Single(await LoadKeysAsync(submitted.Verifier.User.Id));
        DeathCaseDecision decision = await LoadFinalDecisionAsync(submitted.CaseId);
        IDecisionSigner signer = factory.Services.GetRequiredService<IDecisionSigner>();
        VerifierSigningKey attackerKey = signer.CreateKey(Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] forgedSignature = signer.Sign(attackerKey, await BuildPayloadFromDatabaseAsync(decision));
        byte[] attackerPublicKey = attackerKey.PublicKey;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<VerifierSigningKey>().Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.PublicKey, attackerPublicKey));
            await db.Set<DeathCaseDecision>().Where(d => d.Id == decision.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Signature, forgedSignature));
        });

        // Hành động
        JsonElement body = await GetSignatureAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra: chữ ký khớp khóa công khai giả nhưng khóa riêng đã bọc không phải cặp (D7)
        Assert.Equal("INVALID", body.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Death14_OldUnsignedDecisionShowsNotSigned()
    {
        // Chuẩn bị: giả lập quyết định cũ trước khi có chữ ký số
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage reject = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseDecision>().Where(d => d.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Signature, (byte[]?)null));
        });

        // Hành động
        JsonElement body = await GetSignatureAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal("NOT_SIGNED", body.GetProperty("status").GetString());
        Assert.Equal("REJECTED", body.GetProperty("decisionKind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("signatureBase64").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("algorithm").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("keyFingerprint").ValueKind);
    }

    [SqlFact]
    public async Task Death14_CaseWithoutFinalDecisionShowsNoFinalDecision()
    {
        // Chuẩn bị: hồ sơ mới có yêu cầu bổ sung, chưa có quyết định cuối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Cần bản chụp rõ hơn.", new[] { "FILES" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);

        // Hành động
        JsonElement body = await GetSignatureAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal("NO_FINAL_DECISION", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("decisionKind").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("signedByUserId").ValueKind);
    }

    [SqlFact]
    public async Task Sec04_OtherVerifierCannotSeeSignature()
    {
        // Chuẩn bị: hồ sơ đã duyệt; một Verifier khác không được giao
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        DeathCaseReviewTestData.VerifierClient other = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);

        // Hành động
        HttpResponseMessage otherResponse = await other.Client.GetAsync(SignatureUrl(submitted.CaseId));
        HttpResponseMessage executorResponse = await submitted.Executor.Client.GetAsync(SignatureUrl(submitted.CaseId));
        HttpResponseMessage unknownResponse = await submitted.Verifier.Client.GetAsync(SignatureUrl(Guid.NewGuid()));

        // Kiểm tra: Verifier khác và hồ sơ không có đều 404 giống nhau; Executor không phải Verifier → 403
        await ApiTestHelpers.AssertProblemAsync(otherResponse, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(unknownResponse, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(executorResponse, HttpStatusCode.Forbidden, "ROLE_NOT_ALLOWED");
    }

    [SqlFact]
    public async Task Death14_ResponseHasNoPrivateKeyOrPassword()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);
        string approveText = await approve.Content.ReadAsStringAsync();
        VerifierSigningKey key = Assert.Single(await LoadKeysAsync(submitted.Verifier.User.Id));

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(SignatureUrl(submitted.CaseId));
        string text = await response.Content.ReadAsStringAsync();
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra: đúng 9 trường đã công bố; không lộ mật khẩu, khóa riêng đã mã hóa, DEK đã bọc, id KEK
        List<string> names = new List<string>();
        foreach (JsonProperty property in body.EnumerateObject())
        {
            names.Add(property.Name);
        }
        names.Sort(StringComparer.Ordinal);
        Assert.Equal(new List<string>
        {
            "algorithm", "decidedAt", "decisionKind", "keyFingerprint", "signatureBase64",
            "signedByFullName", "signedByUserId", "status", "versionNo",
        }, names);
        foreach (string responseText in new[] { text, approveText })
        {
            Assert.DoesNotContain(DeathCaseTestData.TestPassword, responseText);
            Assert.DoesNotContain(Convert.ToBase64String(key.PrivateKeyEncrypted), responseText);
            Assert.DoesNotContain(Convert.ToBase64String(key.WrappedDataKey), responseText);
            Assert.DoesNotContain("privateKey", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", responseText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [SqlFact]
    public async Task Death14_CaseDetailShowsValidSignatureAfterApprove()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, ApproveReason);
        JsonElement approveBody = await ApiTestHelpers.ReadJsonAsync(approve);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra: cả response của lệnh Duyệt và GET chi tiết đều tự kiểm ra VALID
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("VALID", approveBody.GetProperty("decisionSignatureStatus").GetString());
        Assert.Equal("VALID", detail.GetProperty("decisionSignatureStatus").GetString());
    }

    [SqlFact]
    public async Task Death14_CaseDetailShowsInvalidAfterTampering()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpResponseMessage reject = await DeathCaseReviewTestData.RejectNowAsync(submitted, RejectReason);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseDecision>().Where(d => d.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Reason, "Lý do đã bị sửa."));
        });

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal("INVALID", detail.GetProperty("decisionSignatureStatus").GetString());
    }

    [SqlFact]
    public async Task Death14_CaseDetailUnderReviewShowsNoFinalDecision()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Kiểm tra
        Assert.Equal("UNDER_REVIEW", detail.GetProperty("status").GetString());
        Assert.Equal("NO_FINAL_DECISION", detail.GetProperty("decisionSignatureStatus").GetString());
    }

    private static string SignatureUrl(Guid caseId)
    {
        return DeathCaseReviewTestData.VerifierCaseUrl(caseId) + "/decision-signature";
    }

    private static async Task<JsonElement> GetSignatureAsync(HttpClient client, Guid caseId)
    {
        HttpResponseMessage response = await client.GetAsync(SignatureUrl(caseId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
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

    /// <summary>Nhập sai đủ 5 lần để bị khóa ký; trả ProblemDetails 423 của lần thứ 5.</summary>
    private static async Task<JsonElement> LockSigningAsync(HttpClient client, Guid caseId)
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

    private async Task<List<VerifierSigningKey>> LoadKeysAsync(Guid verifierId)
    {
        List<VerifierSigningKey> keys = new List<VerifierSigningKey>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            keys = await db.Set<VerifierSigningKey>().AsNoTracking().Where(k => k.UserId == verifierId).ToListAsync();
        });
        return keys;
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

    /// <summary>Dựng lại chuỗi ký từ dữ liệu đã lưu (tệp của bản được xét theo Position) rồi kiểm bằng IDecisionSigner.</summary>
    private async Task<bool> VerifyFromDatabaseAsync(DeathCaseDecision decision, VerifierSigningKey key)
    {
        byte[] payload = await BuildPayloadFromDatabaseAsync(decision);
        IDecisionSigner signer = factory.Services.GetRequiredService<IDecisionSigner>();
        return signer.Verify(key, payload, decision.Signature!);
    }

    private async Task<byte[]> BuildPayloadFromDatabaseAsync(DeathCaseDecision decision)
    {
        DeathCaseVersion? version = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            version = await db.Set<DeathCaseVersion>().AsNoTracking().Include(v => v.Files)
                .SingleAsync(v => v.CaseId == decision.CaseId && v.VersionNo == decision.VersionNo);
        });
        Assert.NotNull(version);
        Assert.NotEmpty(version.Files);
        return DecisionSignatureRules.BuildPayload(decision, DecisionSignatureRules.FileIdsInPosition(version));
    }
}
