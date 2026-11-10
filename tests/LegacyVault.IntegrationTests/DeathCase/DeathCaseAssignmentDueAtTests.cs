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
    public async Task Assign04_ResubmitRecalculatesDueAtFromTheResubmitTime()
    {
        // Chuẩn bị: hồ sơ đã gửi và đang có Verifier giữ
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động: Executor gửi lại bản mới
        DateTimeOffset before = DateTimeOffset.UtcNow;
        await DeathCaseReviewTestData.ResubmitAsync(submitted);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Kiểm tra: hạn = lúc gửi lại + 48 giờ (không còn mốc phản đối nào làm hạn muộn hơn)
        DeathCaseAssignment? active = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, submitted.CaseId);
        Assert.NotNull(active);
        Assert.InRange(active.DueAt, before.AddHours(48), after.AddHours(48));
    }}
