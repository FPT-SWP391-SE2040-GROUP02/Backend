using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Ràng buộc DB và truy vấn của DeathCaseRepository trên SQL Server thật (database tạm, xem ContainerFlowTests).
/// </summary>
public sealed class DeathCaseRepositoryTests : IAsyncLifetime
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
    public async Task Death05_Data04_DatabaseRejectsSecondOpenCaseForVault()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User executor = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        bool firstAdded = await TryAddAsync(DeathCaseTestData.NewDraftCase(vault, executor.Id));

        // Hành động: hồ sơ Draft cũng tính là đang mở (D1)
        bool secondAdded = await TryAddAsync(DeathCaseTestData.NewDraftCase(vault, executor.Id));

        // Kiểm tra
        Assert.True(firstAdded);
        Assert.False(secondAdded);
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<DeathCase>().CountAsync(c => c.VaultId == vault.VaultId);
        });
        Assert.Equal(1, count);
    }

    [SqlFact]
    public async Task Death05_RejectedCaseDoesNotBlockNewCase()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User executor = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        DeathCase rejected = DeathCaseTestData.NewDraftCase(vault, executor.Id);
        await TryAddAsync(rejected);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == rejected.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, DeathCaseStatus.Rejected));
        });
        DeathCase second = DeathCaseTestData.NewDraftCase(vault, executor.Id);

        // Hành động
        bool added = await TryAddAsync(second);
        DeathCase? open = null;
        DeathCase? latestRejected = null;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            open = await repository.FindOpenByVaultIdAsync(vault.VaultId, CancellationToken.None);
            latestRejected = await repository.FindLatestRejectedByVaultIdAsync(vault.VaultId, CancellationToken.None);
        }

        // Kiểm tra
        Assert.True(added);
        Assert.NotNull(open);
        Assert.Equal(second.Id, open.Id);
        Assert.Single(open.Versions);
        Assert.NotNull(latestRejected);
        Assert.Equal(rejected.Id, latestRejected.Id);
    }

    [SqlFact]
    public async Task Assign01_PicksVerifierWithOldestLastAssignment()
    {
        // Chuẩn bị: A được giao 2 ngày trước, B 1 ngày trước, C chưa từng được giao
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCase deathCase = DeathCaseTestData.NewDraftCase(vault, vault.Owner.Id);
        await TryAddAsync(deathCase);
        User verifierA = await DeathCaseTestData.AddVerifierAsync(factory);
        User verifierB = await DeathCaseTestData.AddVerifierAsync(factory);
        User verifierC = await DeathCaseTestData.AddVerifierAsync(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await DeathCaseTestData.AddEndedAssignmentAsync(factory, deathCase.Id, verifierA.Id, now.AddDays(-2));
        await DeathCaseTestData.AddEndedAssignmentAsync(factory, deathCase.Id, verifierB.Id, now.AddDays(-1));

        // Hành động
        Guid? first = await FindCandidateAsync(new List<Guid>());
        List<Guid> excludeC = new List<Guid>();
        excludeC.Add(verifierC.Id);
        Guid? second = await FindCandidateAsync(excludeC);

        // Kiểm tra
        Assert.Equal(verifierC.Id, first);
        Assert.Equal(verifierA.Id, second);
    }

    [SqlFact]
    public async Task Assign01_Role03_SkipsInactiveAndExcludedUsers()
    {
        // Chuẩn bị: Verifier bị khóa, user thường, Verifier bị loại (Owner/Executor/người gửi) đều chưa từng được giao;
        // chỉ còn Verifier hợp lệ đã được giao trước đó.
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCase deathCase = DeathCaseTestData.NewDraftCase(vault, vault.Owner.Id);
        await TryAddAsync(deathCase);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Locked);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Disabled);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User excluded = await DeathCaseTestData.AddVerifierAsync(factory);
        User eligible = await DeathCaseTestData.AddVerifierAsync(factory);
        await DeathCaseTestData.AddEndedAssignmentAsync(factory, deathCase.Id, eligible.Id, DateTimeOffset.UtcNow.AddDays(-1));
        List<Guid> excludedIds = new List<Guid>();
        excludedIds.Add(excluded.Id);
        excludedIds.Add(vault.Owner.Id);

        // Hành động
        Guid? candidate = await FindCandidateAsync(excludedIds);

        // Kiểm tra
        Assert.Equal(eligible.Id, candidate);
    }

    [SqlFact]
    public async Task Assign01_NoVerifierReturnsNull()
    {
        // Chuẩn bị: chỉ có Owner (user thường) và một Verifier bị loại
        await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User excluded = await DeathCaseTestData.AddVerifierAsync(factory);
        List<Guid> excludedIds = new List<Guid>();
        excludedIds.Add(excluded.Id);

        // Hành động
        Guid? candidate = await FindCandidateAsync(excludedIds);

        // Kiểm tra
        Assert.Null(candidate);
    }

    [SqlFact]
    public async Task Death10_FindsCheckInAtOrAfterCutoffOnly()
    {
        // Chuẩn bị: ngày mất 01/10/2026 → mốc 17:00 UTC 01/10 (00:00 giờ VN 02/10)
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.OwnerVault otherVault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DateTimeOffset cutoff = new DateTimeOffset(2026, 10, 1, 17, 0, 0, TimeSpan.Zero);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, cutoff.AddDays(-20), cutoff.AddSeconds(-1));
        // Kho khác điểm danh sau mốc: không được tính cho kho này.
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, otherVault.PlanId, 1, cutoff.AddDays(-20), cutoff.AddHours(1));

        // Hành động
        bool beforeOnly = await HasCheckInAsync(vault.PlanId, cutoff);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 2, cutoff.AddSeconds(-1), cutoff);
        bool atCutoff = await HasCheckInAsync(vault.PlanId, cutoff);

        // Kiểm tra
        Assert.False(beforeOnly);
        Assert.True(atCutoff);
    }

    private async Task<bool> TryAddAsync(DeathCase deathCase)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            return await repository.TryAddAsync(deathCase, CancellationToken.None);
        }
    }

    private async Task<Guid?> FindCandidateAsync(List<Guid> excludedUserIds)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            return await repository.FindVerifierCandidateAsync(excludedUserIds, CancellationToken.None);
        }
    }

    private async Task<bool> HasCheckInAsync(Guid planId, DateTimeOffset cutoff)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            return await repository.HasCheckInAtOrAfterAsync(planId, cutoff, CancellationToken.None);
        }
    }
}
