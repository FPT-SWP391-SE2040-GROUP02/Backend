using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Ràng buộc DB và truy vấn của DeathCaseReviewRepository trên SQL Server thật (database tạm, xem DeathCaseRepositoryTests).
/// </summary>
public sealed class DeathCaseReviewRepositoryTests : IAsyncLifetime
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
    public async Task Death03_Ac71_DatabaseRejectsSecondFinalDecision()
    {
        // Chuẩn bị: hồ sơ đã có một quyết định cuối (từ chối)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, submitted.CaseId, DeathCaseDecisionKind.Rejected, submitted.Verifier.User.Id, submitted.AssignmentId);

        // Hành động: thêm tiếp quyết định cuối thứ hai (duyệt, rồi lại từ chối)
        DbUpdateException? approvedError = await TryAddDecisionAsync(submitted, DeathCaseDecisionKind.Approved);
        DbUpdateException? rejectedAgainError = await TryAddDecisionAsync(submitted, DeathCaseDecisionKind.Rejected);

        // Kiểm tra: DB từ chối cả hai bằng vi phạm chỉ mục unique; chỉ còn một dòng
        Assert.NotNull(approvedError);
        Assert.NotNull(rejectedAgainError);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            Assert.True(repository.IsUniqueViolation(approvedError));
            Assert.True(repository.IsUniqueViolation(rejectedAgainError));
        }
        int count = await CountDecisionsAsync(submitted.CaseId);
        Assert.Equal(1, count);
    }

    [SqlFact]
    public async Task Death01_ManySupplementDecisionsAllowed()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động: ba lần yêu cầu bổ sung, rồi một quyết định cuối
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, submitted.CaseId, DeathCaseDecisionKind.SupplementRequested, submitted.Verifier.User.Id, submitted.AssignmentId);
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, submitted.CaseId, DeathCaseDecisionKind.SupplementRequested, submitted.Verifier.User.Id, submitted.AssignmentId);
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, submitted.CaseId, DeathCaseDecisionKind.SupplementRequested, submitted.Verifier.User.Id, submitted.AssignmentId);
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, submitted.CaseId, DeathCaseDecisionKind.Approved, submitted.Verifier.User.Id, submitted.AssignmentId);

        // Kiểm tra
        int count = await CountDecisionsAsync(submitted.CaseId);
        Assert.Equal(4, count);
    }

    [SqlFact]
    public async Task Sec05_PageListsOnlyCasesWithMyActiveAssignment()
    {
        // Chuẩn bị: hồ sơ 1 giao cho V1; hồ sơ 2 giao cho V2 (V1 đã từng được giao nên V2 đứng trước xoay vòng)
        DeathCaseReviewTestData.SubmittedCase first = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        DeathCaseReviewTestData.SubmittedCase second = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        Assert.NotEqual(first.Verifier.User.Id, second.Verifier.User.Id);

        // Hành động
        List<VerifierCaseRow> firstItems = await FindPageAsync(first.Verifier.User.Id, "dueAt", true);
        List<VerifierCaseRow> secondItems = await FindPageAsync(second.Verifier.User.Id, "dueAt", true);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, first.CaseId, DateTimeOffset.UtcNow);
        List<VerifierCaseRow> firstAfterEnd = await FindPageAsync(first.Verifier.User.Id, "dueAt", true);

        // Kiểm tra: chỉ thấy hồ sơ của mình; phân công đã kết thúc thì không còn trong danh sách
        Assert.Single(firstItems);
        Assert.Equal(first.CaseId, firstItems[0].Id);
        Assert.Equal("UNDER_REVIEW", ToApi(firstItems[0].Status));
        Assert.Equal(1, firstItems[0].ReviewVersionNo);
        Assert.Equal(DeathCaseApi.CitizenId.Substring(8), firstItems[0].OwnerCitizenIdLast4);
        Assert.NotNull(firstItems[0].SubmittedAt);
        Assert.Null(firstItems[0].ClosedAt);
        Assert.Single(secondItems);
        Assert.Equal(second.CaseId, secondItems[0].Id);
        Assert.Empty(firstAfterEnd);
    }

    [SqlFact]
    public async Task Sec05_OpenCasesComeBeforeClosedCases()
    {
        // Chuẩn bị: ba hồ sơ cùng giao cho một Verifier (chỉ có một Verifier); A giao sớm nhất rồi bị đóng
        DeathCaseReviewTestData.SubmittedCase caseA = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        DeathCaseReviewTestData.SubmittedCase caseB = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        DeathCaseReviewTestData.SubmittedCase caseC = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        Guid verifierId = caseA.Verifier.User.Id;
        Assert.Equal(verifierId, caseB.Verifier.User.Id);
        Assert.Equal(verifierId, caseC.Verifier.User.Id);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == caseA.CaseId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, DeathCaseStatus.Rejected)
                    .SetProperty(c => c.ClosedAt, DateTimeOffset.UtcNow));
        });

        // Hành động
        List<VerifierCaseRow> byDueAscending = await FindPageAsync(verifierId, "dueAt", true);
        List<VerifierCaseRow> byDueDescending = await FindPageAsync(verifierId, "dueAt", false);
        List<VerifierCaseRow> byAssignedAscending = await FindPageAsync(verifierId, "assignedAt", true);

        // Kiểm tra: hồ sơ còn mở trước (theo hạn xét), hồ sơ đã đóng cuối dù giao sớm nhất
        Assert.Equal(new List<Guid> { caseB.CaseId, caseC.CaseId, caseA.CaseId }, IdsOf(byDueAscending));
        Assert.Equal(new List<Guid> { caseC.CaseId, caseB.CaseId, caseA.CaseId }, IdsOf(byDueDescending));
        Assert.Equal(new List<Guid> { caseB.CaseId, caseC.CaseId, caseA.CaseId }, IdsOf(byAssignedAscending));
        Assert.NotNull(byDueAscending[2].ClosedAt);
    }

    [SqlFact]
    public async Task Sec05_FindSubmittedFileIgnoresDraftAndOtherCase()
    {
        // Chuẩn bị: hồ sơ 1 đã gửi, Executor tạo bản nháp số 2 (chép lại các trang); hồ sơ 2 là hồ sơ khác
        DeathCaseReviewTestData.SubmittedCase caseOne = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        DeathCaseReviewTestData.SubmittedCase caseTwo = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        Guid submittedFileId = caseOne.SubmittedBody.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();
        HttpResponseMessage created = await caseOne.Executor.Client.PostAsJsonAsync(
            DeathCaseApi.CaseUrl(caseOne.CaseId) + "/versions",
            new { etag = caseOne.SubmittedBody.GetProperty("etag").GetString() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement withDraft = await ApiTestHelpers.ReadJsonAsync(created);
        Assert.True(withDraft.GetProperty("currentVersion").GetProperty("isDraft").GetBoolean());
        Guid draftFileId = withDraft.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();
        Assert.NotEqual(submittedFileId, draftFileId);

        // Hành động
        DeathCaseFile? submittedFile = await FindFileAsync(caseOne.CaseId, submittedFileId);
        DeathCaseFile? draftFile = await FindFileAsync(caseOne.CaseId, draftFileId);
        DeathCaseFile? otherCaseFile = await FindFileAsync(caseTwo.CaseId, submittedFileId);

        // Kiểm tra: chỉ trang của bản đã gửi, đúng hồ sơ
        Assert.NotNull(submittedFile);
        Assert.Equal(submittedFileId, submittedFile.Id);
        Assert.Null(draftFile);
        Assert.Null(otherCaseFile);
    }

    [SqlFact]
    public async Task Death12_FindPreviousCaseReturnsRejectReason()
    {
        // Chuẩn bị: hồ sơ bị từ chối có lý do; một hồ sơ khác chưa có quyết định
        DeathCaseReviewTestData.SubmittedCase rejectedCase = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 1);
        DeathCaseReviewTestData.SubmittedCase openCase = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);
        await DeathCaseReviewTestData.AddFinalDecisionAsync(
            factory, rejectedCase.CaseId, DeathCaseDecisionKind.Rejected, rejectedCase.Verifier.User.Id, rejectedCase.AssignmentId);
        DateTimeOffset closedAt = DateTimeOffset.UtcNow;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == rejectedCase.CaseId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, DeathCaseStatus.Rejected)
                    .SetProperty(c => c.ClosedAt, closedAt));
        });

        // Hành động
        PreviousCaseRow? previous = null;
        PreviousCaseRow? withoutDecision = null;
        PreviousCaseRow? missing = null;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseReviewRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseReviewRepository>();
            previous = await repository.FindPreviousCaseAsync(rejectedCase.CaseId, CancellationToken.None);
            withoutDecision = await repository.FindPreviousCaseAsync(openCase.CaseId, CancellationToken.None);
            missing = await repository.FindPreviousCaseAsync(Guid.NewGuid(), CancellationToken.None);
        }

        // Kiểm tra
        Assert.NotNull(previous);
        Assert.Equal(rejectedCase.CaseId, previous.Id);
        Assert.Equal("Lý do thử", previous.RejectReason);
        Assert.NotNull(previous.ClosedAt);
        Assert.NotNull(withoutDecision);
        Assert.Null(withoutDecision.RejectReason);
        Assert.Null(missing);
    }

    [SqlFact]
    public async Task Sec05_HelperReturnsActuallyAssignedVerifier()
    {
        // Chuẩn bị: hồ sơ 1 có hai Verifier sẵn, một người được giao (người kia chưa) → người đó đã từng được giao
        DeathCaseReviewTestData.SubmittedCase first = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 2);
        Guid firstAssignedId = first.Verifier.User.Id;

        // Hành động: hồ sơ 2 gửi sau; xoay vòng phải chọn người chưa từng được giao
        DeathCaseReviewTestData.SubmittedCase second = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory, 0);

        // Kiểm tra: helper trả đúng người có dòng phân công đang hiệu lực của từng hồ sơ
        Guid? firstActive = await FindActiveAssigneeAsync(first.CaseId);
        Guid? secondActive = await FindActiveAssigneeAsync(second.CaseId);
        Assert.Equal(firstActive, first.Verifier.User.Id);
        Assert.Equal(secondActive, second.Verifier.User.Id);
        Assert.NotEqual(firstAssignedId, second.Verifier.User.Id);
        Assert.NotEqual(Guid.Empty, second.AssignmentId);
    }

    private static string ToApi(DeathCaseStatus status)
    {
        return LegacyVault.Application.Mappings.EnumText.ToApiString(status);
    }

    private static List<Guid> IdsOf(List<VerifierCaseRow> rows)
    {
        List<Guid> ids = new List<Guid>();
        foreach (VerifierCaseRow row in rows)
        {
            ids.Add(row.Id);
        }
        return ids;
    }

    private async Task<List<VerifierCaseRow>> FindPageAsync(Guid verifierUserId, string sortBy, bool isAscending)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseReviewRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseReviewRepository>();
            (List<VerifierCaseRow> items, int totalCount) = await repository.FindAssignedPageAsync(
                verifierUserId, sortBy, isAscending, 1, 50, CancellationToken.None);
            Assert.Equal(items.Count, totalCount);
            return items;
        }
    }

    private async Task<DeathCaseFile?> FindFileAsync(Guid caseId, Guid fileId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseReviewRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseReviewRepository>();
            return await repository.FindSubmittedFileAsync(caseId, fileId, CancellationToken.None);
        }
    }

    private async Task<Guid?> FindActiveAssigneeAsync(Guid caseId)
    {
        Guid? assignee = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            assignee = await db.Set<DeathCaseAssignment>()
                .Where(a => a.CaseId == caseId && a.EndedAt == null)
                .Select(a => (Guid?)a.VerifierUserId)
                .SingleOrDefaultAsync();
        });
        return assignee;
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

    /// <summary>Thêm một quyết định qua repository rồi lưu; trả lỗi lưu (null nếu lưu được).</summary>
    private async Task<DbUpdateException?> TryAddDecisionAsync(
        DeathCaseReviewTestData.SubmittedCase submitted, DeathCaseDecisionKind kind)
    {
        DeathCaseDecision decision = new DeathCaseDecision();
        decision.CaseId = submitted.CaseId;
        decision.VersionNo = 1;
        decision.Kind = kind;
        decision.Reason = "Quyết định thử";
        decision.VerifierUserId = submitted.Verifier.User.Id;
        decision.AssignmentId = submitted.AssignmentId;
        decision.DecidedAt = DateTimeOffset.UtcNow;

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseReviewRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseReviewRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.AddDecision(decision);
            try
            {
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return null;
            }
            catch (DbUpdateException ex)
            {
                return ex;
            }
        }
    }
}
