using LegacyVault.Domain.Entities;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Hạn xét của phân công khi Executor gửi lại lúc đồng hồ phản đối còn chạy (ASSIGN-04, TIME-07, D1).</summary>
public sealed class DeathCaseAssignmentDueAtTests : IAsyncLifetime
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
    public async Task Assign04_ResubmitKeepsDueAtAfterObjectionEnds()
    {
        // Chuẩn bị: Owner nhận thư cách đây 10 giờ nên phản đối còn khoảng 62 giờ nữa mới hết
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.MarkOwnerNotifiedAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow.AddHours(-10));
        DateTimeOffset objectionEndsAt = DateTimeOffset.MinValue;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase deathCase = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            objectionEndsAt = deathCase.ObjectionEndsAt!.Value;
        });

        // Hành động: Executor gửi lại, đổi không trọng yếu nên đồng hồ phản đối giữ nguyên
        await DeathCaseReviewTestData.ResubmitAsync(submitted);

        // Kiểm tra: hạn = hết phản đối + 48 giờ (muộn hơn lúc gửi + 48 giờ)
        DeathCaseAssignment? active = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(active);
        Assert.Equal(objectionEndsAt.AddHours(48), active.DueAt);
    }
}
