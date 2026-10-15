using System.Net;
using System.Text;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Verifier xem danh sách, chi tiết và trang giấy chứng tử qua HTTP trên SQL Server thật
/// (SEC-05, ROLE-04, DATA-02, DATA-06, DEATH-02, DEATH-04, DEATH-12).
/// </summary>
public sealed class VerifierReadFlowTests : IAsyncLifetime
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
    public async Task Sec05_NonVerifierGetsRoleNotAllowed()
    {
        // Chuẩn bị: Executor (vai trò User) gọi API của Verifier
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage list = await submitted.Executor.Client.GetAsync(DeathCaseReviewTestData.VerifierListUrl);
        HttpResponseMessage detail = await submitted.Executor.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(list, HttpStatusCode.Forbidden, "ROLE_NOT_ALLOWED");
        await ApiTestHelpers.AssertProblemAsync(detail, HttpStatusCode.Forbidden, "ROLE_NOT_ALLOWED");
    }

    [SqlFact]
    public async Task Role04_VerifierDemotedToUserGetsRoleNotAllowed()
    {
        // Chuẩn bị: token vẫn mang vai trò Verifier nhưng DB đã đổi sang User
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid verifierId = submitted.Verifier.User.Id;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == verifierId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.User));
        });

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));

        // Kiểm tra: service đọc vai trò từ DB, không tin claim
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "ROLE_NOT_ALLOWED");
    }

    [SqlFact]
    public async Task Role04_InactiveVerifierGetsAccountNotActive()
    {
        // Chuẩn bị: Verifier bị khóa sau khi token được cấp
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid verifierId = submitted.Verifier.User.Id;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == verifierId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Locked));
        });

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierListUrl);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "ACCOUNT_NOT_ACTIVE");
    }

    [SqlFact]
    public async Task Sec05_UnassignedVerifierGetsNotFound()
    {
        // Chuẩn bị: Verifier khác được tạo SAU khi Executor gửi nên không được giao
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.VerifierClient other = await DeathCaseReviewTestData.AddVerifierClientAsync(factory);
        Guid fileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));

        // Hành động
        HttpResponseMessage detail = await other.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));
        HttpResponseMessage file = await other.Client.GetAsync(DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, fileId));
        HttpResponseMessage list = await other.Client.GetAsync(DeathCaseReviewTestData.VerifierListUrl);

        // Kiểm tra: 404 giống hồ sơ không tồn tại; danh sách rỗng
        await ApiTestHelpers.AssertProblemAsync(detail, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(file, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(list);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        HttpResponseMessage missing = await other.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(Guid.NewGuid()));
        await ApiTestHelpers.AssertProblemAsync(missing, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    [SqlFact]
    public async Task Sec05_EndedAssignmentGetsNotFound()
    {
        // Chuẩn bị: phân công đã kết thúc (giao lại người khác)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid fileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);

        // Hành động
        HttpResponseMessage list = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierListUrl);
        HttpResponseMessage detail = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));
        HttpResponseMessage file = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, fileId));

        // Kiểm tra: cả 3 API đọc đều không còn thấy hồ sơ
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(list);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        await ApiTestHelpers.AssertProblemAsync(detail, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(file, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    [SqlFact]
    public async Task Data02_ListHasOnlyCitizenIdLast4()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierListUrl);
        string raw = await response.Content.ReadAsStringAsync();

        // Kiểm tra: JSON thô không chứa 12 số CCCD hay họ tên Owner
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(DeathCaseApi.CitizenId, raw);
        Assert.DoesNotContain("Nguyễn Văn An", raw);
        JsonElement page = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        JsonElement row = page.GetProperty("items")[0];
        Assert.Equal(submitted.CaseId, row.GetProperty("id").GetGuid());
        Assert.Equal("UNDER_REVIEW", row.GetProperty("status").GetString());
        Assert.Equal("8901", row.GetProperty("ownerCitizenIdLast4").GetString());
        Assert.Equal(1, row.GetProperty("reviewVersionNo").GetInt32());
        Assert.Equal(0, row.GetProperty("unreviewedFlagCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("closedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("dueAt").ValueKind);
    }

    [SqlFact]
    public async Task Data02_DetailHasFullCitizenIdOnlyForReviewVersion()
    {
        // Chuẩn bị: bản 1 đã gửi, Executor gửi tiếp bản 2 nên Verifier xét bản 2
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.ResubmitAsync(submitted);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement review = detail.GetProperty("reviewVersion");
        Assert.Equal(2, review.GetProperty("versionNo").GetInt32());
        Assert.Equal(DeathCaseApi.CitizenId, review.GetProperty("ownerCitizenId").GetString());
        Assert.Equal("8901", review.GetProperty("ownerCitizenIdLast4").GetString());
        Assert.Equal("Nguyễn Văn An", review.GetProperty("ownerFullName").GetString());
        Assert.Equal("123/2026/TT", review.GetProperty("certificateNumber").GetString());
        Assert.Equal("UBND phường Bến Thành", review.GetProperty("issuedBy").GetString());

        JsonElement older = detail.GetProperty("versions");
        Assert.Equal(1, older.GetArrayLength());
        JsonElement firstVersion = older[0];
        Assert.Equal(1, firstVersion.GetProperty("versionNo").GetInt32());
        Assert.Equal("8901", firstVersion.GetProperty("ownerCitizenIdLast4").GetString());
        Assert.False(firstVersion.TryGetProperty("ownerCitizenId", out JsonElement _));
        Assert.False(firstVersion.TryGetProperty("ownerFullName", out JsonElement _));
        Assert.False(firstVersion.TryGetProperty("certificateNumber", out JsonElement _));
        Assert.False(firstVersion.TryGetProperty("issuedBy", out JsonElement _));
        Assert.DoesNotContain(DeathCaseApi.CitizenId, firstVersion.GetRawText());
    }

    [SqlFact]
    public async Task Death02_DetailHasNoExecutorOrStorageInfoAndIsNoStore()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(submitted.CaseId));
        string raw = await response.Content.ReadAsStringAsync();

        // Kiểm tra: không thông tin Executor, email Owner, khóa lưu trữ, SHA-256; không cho lưu đệm
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(submitted.Executor.User.Id.ToString(), raw);
        Assert.DoesNotContain(submitted.Executor.User.Email, raw);
        Assert.DoesNotContain(submitted.Vault.Owner.Email, raw);
        Assert.DoesNotContain(submitted.Vault.Owner.Id.ToString(), raw);
        Assert.DoesNotContain(submitted.Verifier.User.Id.ToString(), raw);
        Assert.DoesNotContain("storageKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executor", raw, StringComparison.OrdinalIgnoreCase);
        JsonElement detail = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal("UNDER_REVIEW", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canApprove").GetBoolean());
        Assert.Contains("OWNER_NOT_NOTIFIED", DeathCaseApi.ReadStrings(detail.GetProperty("approveBlockers")));
        Assert.Equal(0, detail.GetProperty("decisions").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("previousCase").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("handover").ValueKind);
        Assert.Equal("PENDING", detail.GetProperty("ownerNotice").GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("assignment").GetProperty("dueAt").ValueKind);
    }

    [SqlFact]
    public async Task Death12_DetailShowsPreviousRejectedCase()
    {
        // Chuẩn bị: hồ sơ đầu bị từ chối (có lý do), Executor mở và gửi hồ sơ mới của cùng kho
        DeathCaseReviewTestData.SubmittedCase first = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, first.CaseId, DeathCaseDecisionKind.Rejected, first.Verifier.User.Id, first.AssignmentId);
        DateTimeOffset closedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == first.CaseId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, DeathCaseStatus.Rejected)
                    .SetProperty(c => c.ClosedAt, closedAt));
        });
        await ApiTestHelpers.SetPlanStatusAsync(factory, first.Vault.VaultId, PlanStatus.Active);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            first.Executor.Client, first.Vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submittedSecond = await DeathCaseApi.SubmitOkAsync(first.Executor.Client, prepared);
        Guid secondCaseId = submittedSecond.GetProperty("id").GetGuid();
        Assert.NotEqual(first.CaseId, secondCaseId);

        // Hành động: Verifier (người duy nhất) được giao hồ sơ mới
        HttpResponseMessage response = await first.Verifier.Client.GetAsync(DeathCaseReviewTestData.VerifierCaseUrl(secondCaseId));
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement previous = detail.GetProperty("previousCase");
        Assert.Equal(first.CaseId, previous.GetProperty("id").GetGuid());
        Assert.Equal("Lý do thử", previous.GetProperty("rejectReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, previous.GetProperty("closedAt").ValueKind);
    }

    [SqlFact]
    public async Task Sec05_ListSortsOpenCasesFirstThenDueAt()
    {
        // Chuẩn bị: ba hồ sơ cùng một Verifier (chỉ có một Verifier); A giao sớm nhất rồi bị đóng
        DeathCaseReviewTestData.SubmittedCase caseA = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        DeathCaseReviewTestData.SubmittedCase caseB = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        DeathCaseReviewTestData.SubmittedCase caseC = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        Assert.Equal(caseA.Verifier.User.Id, caseB.Verifier.User.Id);
        Assert.Equal(caseA.Verifier.User.Id, caseC.Verifier.User.Id);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == caseA.CaseId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, DeathCaseStatus.Rejected)
                    .SetProperty(c => c.ClosedAt, DateTimeOffset.UtcNow));
        });
        HttpClient verifier = caseA.Verifier.Client;

        // Hành động: sortBy không phân biệt hoa thường
        List<Guid> byDue = await ListIdsAsync(verifier, "?sortBy=dueAt&isAscending=true");
        List<Guid> byDueDescending = await ListIdsAsync(verifier, "?sortBy=DUEAT&isAscending=false");
        List<Guid> byAssigned = await ListIdsAsync(verifier, "?sortBy=AssignedAt");
        List<Guid> byDefault = await ListIdsAsync(verifier, string.Empty);

        // Kiểm tra: hồ sơ còn mở trước, hồ sơ đã đóng cuối
        Assert.Equal(new List<Guid> { caseB.CaseId, caseC.CaseId, caseA.CaseId }, byDue);
        Assert.Equal(new List<Guid> { caseC.CaseId, caseB.CaseId, caseA.CaseId }, byDueDescending);
        Assert.Equal(new List<Guid> { caseB.CaseId, caseC.CaseId, caseA.CaseId }, byAssigned);
        Assert.Equal(byDue, byDefault);
    }

    [SqlFact]
    public async Task Sec05_UnknownSortByIsRejected()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierListUrl + "?sortBy=createdAt");

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("sortBy", out JsonElement _));
    }

    [SqlFact]
    public async Task Data06_ReviewPageReturnsOriginalBytesAndHeaders()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid fileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, fileId));
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        // Kiểm tra: đúng byte gốc, dạng tải về, không đoán kiểu, không lưu đệm
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedPdfBytes(), body);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("trang.pdf", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [SqlFact]
    public async Task Data06_OlderSubmittedVersionPageIsViewable()
    {
        // Chuẩn bị: bản 2 đã gửi nên bản 1 là bản cũ; trang của bản 1 vẫn xem được
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid firstVersionFileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));
        await DeathCaseReviewTestData.ResubmitAsync(submitted);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, firstVersionFileId));
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedPdfBytes(), body);
    }

    [SqlFact]
    public async Task Death04_CopiedPageViewableFromBothVersions()
    {
        // Chuẩn bị: bản 2 chép trang của bản 1 (dùng chung nội dung đã lưu)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid firstVersionFileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));
        JsonElement resubmitted = await DeathCaseReviewTestData.ResubmitAsync(submitted);
        Guid secondVersionFileId = FirstFileId(resubmitted.GetProperty("currentVersion"));
        Assert.NotEqual(firstVersionFileId, secondVersionFileId);

        // Hành động
        HttpResponseMessage fromFirst = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, firstVersionFileId));
        HttpResponseMessage fromSecond = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, secondVersionFileId));
        byte[] firstBytes = await fromFirst.Content.ReadAsByteArrayAsync();
        byte[] secondBytes = await fromSecond.Content.ReadAsByteArrayAsync();

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, fromFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromSecond.StatusCode);
        Assert.Equal(ExpectedPdfBytes(), firstBytes);
        Assert.Equal(firstBytes, secondBytes);
    }

    [SqlFact]
    public async Task Death04_DraftPageIsNotFound()
    {
        // Chuẩn bị: Executor tạo bản nháp mới (chép trang); trang của bản nháp chưa được gửi
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement withDraft = await DeathCaseReviewTestData.CreateDraftVersionAsync(submitted);
        Guid draftFileId = FirstFileId(withDraft.GetProperty("currentVersion"));
        Assert.True(withDraft.GetProperty("currentVersion").GetProperty("isDraft").GetBoolean());

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, draftFileId));

        // Kiểm tra: Verifier không bao giờ thấy bản nháp
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    [SqlFact]
    public async Task Sec05_PageOfOtherCaseIsNotFound()
    {
        // Chuẩn bị: hai hồ sơ cùng giao cho một Verifier; dùng Id trang của hồ sơ 1 dưới URL hồ sơ 2
        DeathCaseReviewTestData.SubmittedCase caseOne = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        DeathCaseReviewTestData.SubmittedCase caseTwo = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        Assert.Equal(caseOne.Verifier.User.Id, caseTwo.Verifier.User.Id);
        Guid fileOfCaseOne = FirstFileId(caseOne.SubmittedBody.GetProperty("currentVersion"));

        // Hành động
        HttpResponseMessage response = await caseOne.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(caseTwo.CaseId, fileOfCaseOne));

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    [SqlFact]
    public async Task Data06_TamperedPageReturnsIntegrityFailed()
    {
        // Chuẩn bị: sửa một byte của tệp đã mã hóa trên đĩa
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid fileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));
        string path = await StoragePathAsync(fileId);
        byte[] onDisk = await File.ReadAllBytesAsync(path);
        onDisk[onDisk.Length - 1] ^= 0x01;
        await File.WriteAllBytesAsync(path, onDisk);

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, fileId));

        // Kiểm tra: 500 CONTENT_INTEGRITY_FAILED, không có byte nội dung nào
        await AssertIntegrityFailedWithoutContentAsync(response);
    }

    [SqlFact]
    public async Task Data06_MissingObjectReturnsIntegrityFailed()
    {
        // Chuẩn bị: tệp đã mã hóa bị mất khỏi kho lưu trữ
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid fileId = FirstFileId(submitted.SubmittedBody.GetProperty("currentVersion"));
        File.Delete(await StoragePathAsync(fileId));

        // Hành động
        HttpResponseMessage response = await submitted.Verifier.Client.GetAsync(
            DeathCaseReviewTestData.VerifierFileUrl(submitted.CaseId, fileId));

        // Kiểm tra
        await AssertIntegrityFailedWithoutContentAsync(response);
    }

    private static async Task AssertIntegrityFailedWithoutContentAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        JsonElement problem = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("CONTENT_INTEGRITY_FAILED", problem.GetProperty("code").GetString());
        Assert.True(body.Length < 4096);
        byte[] fragment = Encoding.ASCII.GetBytes("%PDF-1.7");
        Assert.True(body.AsSpan().IndexOf(fragment) < 0);
    }

    private async Task<string> StoragePathAsync(Guid fileId)
    {
        string storageKey = string.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            storageKey = await db.Set<DeathCaseFile>().Where(f => f.Id == fileId).Select(f => f.StorageKey).SingleAsync();
        });
        return Path.Combine(factory.StorageRoot, storageKey[^2..], storageKey);
    }

    /// <summary>Đúng nội dung DeathCaseApi.UploadPdfAsync tải lên trong PrepareCompleteDraftAsync.</summary>
    private static byte[] ExpectedPdfBytes()
    {
        return Encoding.ASCII.GetBytes("%PDF-1.7\n" + "trang chung tu" + "\n" + new string('A', 512));
    }

    private static Guid FirstFileId(JsonElement version)
    {
        return version.GetProperty("files")[0].GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync(DeathCaseReviewTestData.VerifierListUrl + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        List<Guid> ids = new List<Guid>();
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
        {
            ids.Add(item.GetProperty("id").GetGuid());
        }
        return ids;
    }
}
