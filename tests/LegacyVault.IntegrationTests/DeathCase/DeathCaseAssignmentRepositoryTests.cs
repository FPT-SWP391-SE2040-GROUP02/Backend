using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Truy vấn chọn hồ sơ cho job phân công trên SQL Server thật (ASSIGN-02, ASSIGN-04, TIME-02, D2, D3, D5, AC-71).
/// </summary>
public sealed class DeathCaseAssignmentRepositoryTests : IAsyncLifetime
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
    public async Task Assign04_FindsUnderReviewCaseWithoutActiveAssignment()
    {
        // Chuẩn bị: hồ sơ đang xét nhưng phân công đã kết thúc, chưa có người thay
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.Contains(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task Assign04_FindsSupplementRequiredCaseWithoutActiveAssignment()
    {
        // Chuẩn bị: hồ sơ chờ bổ sung và không còn ai giữ (D2: vẫn cần giao người)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.Contains(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task Assign04_FindsOverdueUnderReviewCase()
    {
        // Chuẩn bị: Verifier còn hoạt động nhưng hạn đã qua 1 giờ; đã nhắc cho hạn này rồi
        // nên chỉ còn điều kiện quá hạn khiến hồ sơ được chọn
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddHours(-1);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, dueAt);
        await DeathCaseReviewTestData.SetActiveRemindedDueAtAsync(factory, submitted.CaseId, dueAt);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.Contains(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task Assign04_FindsCaseNeedingReminder()
    {
        // Chuẩn bị: hồ sơ mới giao, hạn còn 48 giờ nên chưa cần làm gì
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        List<Guid> before = await FindIdsAsync();

        // Hành động: hạn còn 6 giờ, chưa nhắc
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(6));
        List<Guid> after = await FindIdsAsync();

        // Kiểm tra
        Assert.DoesNotContain(submitted.CaseId, before);
        Assert.Contains(submitted.CaseId, after);
    }

    [SqlFact]
    public async Task Assign04_SkipsAlreadyRemindedCaseBeforeDue()
    {
        // Chuẩn bị: hạn còn 6 giờ và đã nhắc đúng cho hạn này (TIME-02)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddHours(6);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, dueAt);
        await DeathCaseReviewTestData.SetActiveRemindedDueAtAsync(factory, submitted.CaseId, dueAt);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.DoesNotContain(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task D2_SkipsOverdueSupplementRequiredCaseWithActiveVerifier()
    {
        // Chuẩn bị: hồ sơ đang chờ Executor bổ sung, hạn đã qua 5 giờ nhưng Verifier vẫn hoạt động
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddHours(-5);
        await DeathCaseReviewTestData.SetActiveDueAtAsync(factory, submitted.CaseId, dueAt);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra: không nhắc, không giao lại vì quá hạn
        Assert.DoesNotContain(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task Assign02_FindsCaseWhoseVerifierWasDeactivated()
    {
        // Chuẩn bị: hạn còn 48 giờ nhưng Verifier đang giữ bị khóa
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.Contains(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task Assign02_FindsCaseWhoseVerifierLostRole()
    {
        // Chuẩn bị: Verifier đang giữ bị thu hồi vai trò, hồ sơ đang chờ bổ sung (vẫn phải giao lại ngay)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, submitted.CaseId, DeathCaseStatus.SupplementRequired);
        await DeathCaseReviewTestData.SetUserRoleAsync(factory, submitted.Verifier.User.Id, UserRole.User);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.Contains(submitted.CaseId, ids);
    }

    [SqlFact]
    public async Task Ac71_SkipsDraftApprovedAndRejectedCases()
    {
        // Chuẩn bị: ba hồ sơ đã đóng hoặc chưa gửi, phân công đã kết thúc (nếu không lọc trạng thái thì sẽ bị chọn)
        DeathCaseReviewTestData.SubmittedCase draft = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.SubmittedCase approved = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseReviewTestData.SubmittedCase rejected = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, draft.CaseId, DeathCaseStatus.Draft);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, approved.CaseId, DeathCaseStatus.ApprovedForHandover);
        await DeathCaseReviewTestData.SetCaseStatusAsync(factory, rejected.CaseId, DeathCaseStatus.Rejected);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, draft.CaseId, DateTimeOffset.UtcNow);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, approved.CaseId, DateTimeOffset.UtcNow);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, rejected.CaseId, DateTimeOffset.UtcNow);

        // Hành động
        List<Guid> ids = await FindIdsAsync();

        // Kiểm tra
        Assert.DoesNotContain(draft.CaseId, ids);
        Assert.DoesNotContain(approved.CaseId, ids);
        Assert.DoesNotContain(rejected.CaseId, ids);
    }

    private async Task<List<Guid>> FindIdsAsync()
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseAssignmentRepository repository =
                scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentRepository>();
            return await repository.FindCaseIdsForJobAsync(DateTimeOffset.UtcNow, 50, CancellationToken.None);
        }
    }
}
