using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Audit;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Xem trạng thái và điểm danh (CheckInService) bằng repository giả, không cần DB.</summary>
public sealed class CheckInServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodStart = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnds = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly CheckInService service;
    private readonly Guid ownerId = Guid.NewGuid();

    public CheckInServiceTests()
    {
        currentUser.UserId = ownerId;
        service = new CheckInService(currentUser, vaults, repository, new FakeAuditLogRepository(), unitOfWork, clock);
    }

    /// <summary>Tạo kho với kế hoạch ở trạng thái cho trước; withOpenPeriod=true thì thêm kỳ 1 (chu kỳ 30, chờ 7).</summary>
    private Vault SeedVault(PlanStatus planStatus, bool withOpenPeriod)
    {
        Vault vault = CheckInTestData.CreateVault(ownerId, planStatus);
        vaults.Vaults.Add(vault);
        if (withOpenPeriod)
        {
            repository.Periods.Add(CheckInTestData.CreateOpenPeriod(vault.Plan, 1, PeriodStart, 30, 7));
        }
        return vault;
    }

    [Fact]
    public async Task Get_DraftPlan_ReturnsNotStarted()
    {
        SeedVault(PlanStatus.Draft, withOpenPeriod: false);

        CheckInStatusResponse response = await service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None);

        Assert.Equal("NOT_STARTED", response.Status);
        Assert.Equal("DRAFT", response.PlanStatus);
        Assert.Equal(Now, response.ServerTime);
        Assert.Null(response.NextCheckInDueAt);
        Assert.Null(response.GraceEndsAt);
        Assert.Null(response.LastCheckInAt);
        Assert.Empty(response.History.Items);
        Assert.Equal(30, response.Settings.CycleDays);
        Assert.Equal(7, response.Settings.GraceDays);
        Assert.Equal("\"0\"", response.Settings.Etag);
    }

    [Fact]
    public async Task Get_ReportsEffectiveStatusEvenIfJobDidNotRun()
    {
        SeedVault(PlanStatus.Active, withOpenPeriod: true);

        clock.Now = Due;
        CheckInStatusResponse overdue = await service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None);
        clock.Now = GraceEnds;
        CheckInStatusResponse awaiting = await service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None);

        Assert.Equal("CHECKIN_OVERDUE", overdue.Status);
        Assert.Equal("AWAITING_CHECK", awaiting.Status);
        Assert.Equal("ACTIVE", awaiting.PlanStatus);
        Assert.Equal(Due, awaiting.NextCheckInDueAt);
        Assert.Equal(GraceEnds, awaiting.GraceEndsAt);
    }

    [Fact]
    public async Task Get_PausedPlan_ReturnsPaused()
    {
        SeedVault(PlanStatus.DeathCaseOpen, withOpenPeriod: true);

        CheckInStatusResponse response = await service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None);

        Assert.Equal("PAUSED", response.Status);
        Assert.Equal("DEATH_CASE_OPEN", response.PlanStatus);
    }

    [Fact]
    public async Task Get_ReturnsHistoryNewestFirst()
    {
        Vault vault = SeedVault(PlanStatus.Active, withOpenPeriod: false);
        for (int sequence = 1; sequence <= 3; sequence++)
        {
            CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, sequence, PeriodStart, 30, 7);
            if (sequence < 3)
            {
                period.CheckedInAt = PeriodStart;
                period.ClosedAt = PeriodStart;
            }
            repository.Periods.Add(period);
        }

        CheckInStatusResponse response = await service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None);

        Assert.Equal(3, response.History.TotalCount);
        Assert.Equal(3, response.History.Items[0].Sequence);
        Assert.Equal(1, response.History.Items[2].Sequence);
    }

    [Fact]
    public async Task Get_NoVault_ThrowsVaultNotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None));

        Assert.Equal(ErrorCode.VaultNotFound, error.Code);
    }

    [Fact]
    public async Task Get_NotAuthenticated_ThrowsSessionExpired()
    {
        currentUser.UserId = null;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.GetMyCheckInAsync(new PaginationRequest(), CancellationToken.None));

        Assert.Equal(ErrorCode.SessionExpired, error.Code);
    }

    [Theory]
    [InlineData(PlanStatus.Active)]
    [InlineData(PlanStatus.CheckinOverdue)]
    [InlineData(PlanStatus.AwaitingCheck)]
    public async Task CheckIn_FromEachRunningStatus_ReopensActivePeriod(PlanStatus status)
    {
        Vault vault = SeedVault(status, withOpenPeriod: true);

        CheckInStatusResponse response = await service.CheckInAsync(CancellationToken.None);

        CheckInPeriod oldPeriod = repository.Periods.Single(p => p.Sequence == 1);
        Assert.Equal(Now, oldPeriod.CheckedInAt);
        Assert.Equal(Now, oldPeriod.ClosedAt);
        CheckInPeriod newPeriod = Assert.Single(repository.Periods, p => p.ClosedAt == null);
        Assert.Equal(2, newPeriod.Sequence);
        Assert.Equal(Now, newPeriod.StartedAt);
        Assert.Equal(Now.AddDays(30), newPeriod.DueAt);
        Assert.Equal(Now.AddDays(37), newPeriod.GraceEndsAt);
        Assert.Equal(PlanStatus.Active, vault.Plan.Status);
        Assert.Equal(Now, Assert.Single(repository.Schedules).LastCheckInAt);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal("ACTIVE", response.Status);
        Assert.Equal(Now, response.LastCheckInAt);
        Assert.Equal(Now.AddDays(30), response.NextCheckInDueAt);
    }

    [Fact]
    public async Task CheckIn_AppliesPendingSettingsThenClearsThem()
    {
        Vault vault = SeedVault(PlanStatus.Active, withOpenPeriod: true);
        CheckInSchedule schedule = new CheckInSchedule();
        schedule.PlanId = vault.Plan.Id;
        schedule.PendingCycleDays = 60;
        schedule.PendingGraceDays = 14;
        repository.Schedules.Add(schedule);

        await service.CheckInAsync(CancellationToken.None);

        CheckInPeriod newPeriod = Assert.Single(repository.Periods, p => p.ClosedAt == null);
        Assert.Equal(Now.AddDays(60), newPeriod.DueAt);
        Assert.Equal(Now.AddDays(74), newPeriod.GraceEndsAt);
        Assert.Equal(60, schedule.CycleDays);
        Assert.Equal(14, schedule.GraceDays);
        Assert.Null(schedule.PendingCycleDays);
        Assert.Null(schedule.PendingGraceDays);
    }

    [Fact]
    public async Task CheckIn_WithinDuplicateWindow_ReturnsCurrentStateWithoutNewPeriod()
    {
        Vault vault = SeedVault(PlanStatus.Active, withOpenPeriod: true);
        CheckInSchedule schedule = new CheckInSchedule();
        schedule.PlanId = vault.Plan.Id;
        schedule.LastCheckInAt = Now.AddSeconds(-59);
        repository.Schedules.Add(schedule);

        CheckInStatusResponse response = await service.CheckInAsync(CancellationToken.None);

        Assert.Single(repository.Periods);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Equal(Now.AddSeconds(-59), response.LastCheckInAt);
    }

    [Fact]
    public async Task CheckIn_ExactlyAtDuplicateWindowEdge_OpensNewPeriod()
    {
        Vault vault = SeedVault(PlanStatus.Active, withOpenPeriod: true);
        CheckInSchedule schedule = new CheckInSchedule();
        schedule.PlanId = vault.Plan.Id;
        schedule.LastCheckInAt = Now.AddSeconds(-60);
        repository.Schedules.Add(schedule);

        await service.CheckInAsync(CancellationToken.None);

        Assert.Equal(2, repository.Periods.Count);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task CheckIn_RunningPlanWithoutOpenPeriod_OpensFirstPeriod()
    {
        SeedVault(PlanStatus.Active, withOpenPeriod: false);

        CheckInStatusResponse response = await service.CheckInAsync(CancellationToken.None);

        CheckInPeriod period = Assert.Single(repository.Periods);
        Assert.Equal(1, period.Sequence);
        Assert.Equal(Now.AddDays(30), period.DueAt);
        Assert.Equal("ACTIVE", response.Status);
    }

    [Theory]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.DeathCaseOpen)]
    [InlineData(PlanStatus.HandoverInProgress)]
    [InlineData(PlanStatus.Closed)]
    [InlineData(PlanStatus.Blocked)]
    public async Task CheckIn_DraftOrLockedPlan_ThrowsNotAllowed(PlanStatus status)
    {
        SeedVault(status, withOpenPeriod: false);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.CheckInAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.CheckinNotAllowed, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal(status.ToString().ToUpperInvariant(), error.Extensions["planStatus"]!.ToString()!.Replace("_", ""));
        Assert.Empty(repository.Periods);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task CheckIn_AllowedEvenWhenServiceIsGraceReadOnly()
    {
        Vault vault = SeedVault(PlanStatus.CheckinOverdue, withOpenPeriod: true);
        vault.Entitlement.Status = EntitlementStatus.GraceReadOnly;

        CheckInStatusResponse response = await service.CheckInAsync(CancellationToken.None);

        Assert.Equal("ACTIVE", response.Status);
    }
}
