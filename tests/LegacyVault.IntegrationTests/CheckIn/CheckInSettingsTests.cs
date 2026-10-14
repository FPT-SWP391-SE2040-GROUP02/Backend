using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Đổi cấu hình điểm danh (DMS-01) bằng repository giả.</summary>
public sealed class CheckInSettingsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodStart = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly CheckInService service;
    private readonly Guid ownerId = Guid.NewGuid();

    public CheckInSettingsTests()
    {
        currentUser.UserId = ownerId;
        service = new CheckInService(currentUser, vaults, repository, unitOfWork, new FixedTimeProvider(Now));
    }

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

    private static UpdateCheckInSettingsRequest Request(string etag, int? cycleDays, int? graceDays)
    {
        UpdateCheckInSettingsRequest request = new UpdateCheckInSettingsRequest();
        request.Etag = etag;
        request.CycleDays = cycleDays;
        request.GraceDays = graceDays;
        return request;
    }

    [Fact]
    public async Task Update_NoOpenPeriod_AppliesImmediatelyAndCreatesSchedule()
    {
        SeedVault(PlanStatus.Draft, withOpenPeriod: false);

        CheckInStatusResponse response = await service.UpdateSettingsAsync(Request("\"0\"", 60, 14), CancellationToken.None);

        CheckInSchedule schedule = Assert.Single(repository.Schedules);
        Assert.Equal(60, schedule.CycleDays);
        Assert.Equal(14, schedule.GraceDays);
        Assert.Null(schedule.PendingCycleDays);
        Assert.Equal(60, response.Settings.CycleDays);
        Assert.NotEqual("\"0\"", response.Settings.Etag);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_WithOpenPeriod_StoresPendingAndKeepsCurrentDeadline()
    {
        SeedVault(PlanStatus.Active, withOpenPeriod: true);
        CheckInPeriod period = repository.Periods.Single();
        DateTimeOffset dueBefore = period.DueAt;
        DateTimeOffset graceBefore = period.GraceEndsAt;

        CheckInStatusResponse response = await service.UpdateSettingsAsync(Request("\"0\"", 90, 30), CancellationToken.None);

        CheckInSchedule schedule = Assert.Single(repository.Schedules);
        Assert.Equal(30, schedule.CycleDays);
        Assert.Equal(7, schedule.GraceDays);
        Assert.Equal(90, schedule.PendingCycleDays);
        Assert.Equal(30, schedule.PendingGraceDays);
        Assert.Equal(dueBefore, period.DueAt);
        Assert.Equal(graceBefore, period.GraceEndsAt);
        Assert.Equal(90, response.Settings.PendingCycleDays);
        Assert.Equal(dueBefore, response.NextCheckInDueAt);
    }

    [Fact]
    public async Task Update_OnlyOneField_KeepsOther()
    {
        Vault vault = SeedVault(PlanStatus.Draft, withOpenPeriod: false);
        CheckInSchedule schedule = new CheckInSchedule();
        schedule.PlanId = vault.Plan.Id;
        schedule.CycleDays = 60;
        schedule.GraceDays = 7;
        repository.Schedules.Add(schedule);

        await service.UpdateSettingsAsync(Request("\"0\"", null, 14), CancellationToken.None);

        Assert.Equal(60, schedule.CycleDays);
        Assert.Equal(14, schedule.GraceDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(31)]
    [InlineData(45)]
    [InlineData(120)]
    public async Task Update_InvalidCycleDays_ThrowsValidation(int cycleDays)
    {
        SeedVault(PlanStatus.Draft, withOpenPeriod: false);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateSettingsAsync(Request("\"0\"", cycleDays, null), CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
        Assert.Empty(repository.Schedules);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    [InlineData(8)]
    [InlineData(21)]
    [InlineData(60)]
    public async Task Update_InvalidGraceDays_ThrowsValidation(int graceDays)
    {
        SeedVault(PlanStatus.Draft, withOpenPeriod: false);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateSettingsAsync(Request("\"0\"", null, graceDays), CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_EmptyBody_ThrowsValidation()
    {
        SeedVault(PlanStatus.Draft, withOpenPeriod: false);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateSettingsAsync(Request("\"0\"", null, null), CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
    }

    [Theory]
    [InlineData("\"123\"")]
    [InlineData("abc")]
    public async Task Update_StaleOrMalformedEtag_ThrowsEditConflict(string etag)
    {
        SeedVault(PlanStatus.Draft, withOpenPeriod: false);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateSettingsAsync(Request(etag, 60, null), CancellationToken.None));

        Assert.Equal(ErrorCode.EditConflict, error.Code);
        Assert.Empty(repository.Schedules);
    }

    [Theory]
    [InlineData(PlanStatus.DeathCaseOpen)]
    [InlineData(PlanStatus.HandoverInProgress)]
    [InlineData(PlanStatus.Closed)]
    public async Task Update_LockedPlan_ThrowsVaultNotEditablePlanLocked(PlanStatus status)
    {
        SeedVault(status, withOpenPeriod: false);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateSettingsAsync(Request("\"0\"", 60, null), CancellationToken.None));

        Assert.Equal(ErrorCode.VaultNotEditable, error.Code);
        Assert.Equal("PLAN_LOCKED", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Update_ClosedVault_ThrowsVaultClosed()
    {
        Vault vault = SeedVault(PlanStatus.Active, withOpenPeriod: false);
        vault.Status = VaultStatus.Closed;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateSettingsAsync(Request("\"0\"", 60, null), CancellationToken.None));

        Assert.Equal(ErrorCode.VaultNotEditable, error.Code);
        Assert.Equal("VAULT_CLOSED", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Update_BlockedPlan_IsAllowed()
    {
        SeedVault(PlanStatus.Blocked, withOpenPeriod: false);

        CheckInStatusResponse response = await service.UpdateSettingsAsync(Request("\"0\"", 60, null), CancellationToken.None);

        Assert.Equal(60, response.Settings.CycleDays);
    }
}
