using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Audit;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.IntegrationTests.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Người thực thi điểm danh thay chủ kho, có giới hạn số lần liên tiếp do Admin đặt (bằng repository giả).</summary>
public sealed class ExecutorCheckInServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstStart = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeProgressExecutorReader executors = new FakeProgressExecutorReader();
    private readonly FakeDeathCaseRepository deathCases = new FakeDeathCaseRepository();
    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly FakeAuditLogRepository auditLogs = new FakeAuditLogRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly ExecutorCheckInService service;
    private readonly Guid executorUserId = Guid.NewGuid();
    private readonly Guid ownerId = Guid.NewGuid();

    public ExecutorCheckInServiceTests()
    {
        currentUser.UserId = executorUserId;
        service = new ExecutorCheckInService(
            currentUser, vaults, executors, deathCases, repository, auditLogs, unitOfWork, new FixedTimeProvider(Now));
    }

    private Vault SeedOverdueVault(PlanStatus status)
    {
        Vault vault = CheckInTestData.CreateVault(ownerId, status);
        vaults.Vaults.Add(vault);
        Executor executor = new Executor();
        executor.VaultId = vault.Id;
        executor.UserId = executorUserId;
        executor.Status = ExecutorStatus.Accepted;
        executors.Executors.Add(executor);
        return vault;
    }

    /// <summary>Thêm kỳ cũ đã đóng: byExecutor cho biết kỳ do Người thực thi hay chủ kho mở.</summary>
    private void AddClosedPeriod(Vault vault, int sequence, bool byExecutor)
    {
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, sequence, FirstStart.AddDays(sequence), 30);
        period.StartedByExecutor = byExecutor;
        period.ClosedAt = FirstStart.AddDays(sequence + 1);
        repository.Periods.Add(period);
    }

    private void AddOpenPeriod(Vault vault, int sequence, bool byExecutor)
    {
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, sequence, FirstStart.AddDays(sequence), 30);
        period.StartedByExecutor = byExecutor;
        repository.Periods.Add(period);
    }

    [Fact]
    public async Task CheckInForOwner_FirstTime_OpensPeriodAndCountsOne()
    {
        Vault vault = SeedOverdueVault(PlanStatus.AwaitingCheck);
        AddOpenPeriod(vault, 1, byExecutor: false);

        ExecutorCheckInResponse response = await service.CheckInForOwnerAsync(vault.Id, CancellationToken.None);

        Assert.Equal(1, response.ProxyUsed);
        Assert.Equal(2, response.ProxyLimit);
        Assert.Equal(PlanStatus.Active, vault.Plan.Status);
        CheckInPeriod next = Assert.Single(repository.Periods, p => p.ClosedAt == null);
        Assert.True(next.StartedByExecutor);
        Assert.Equal(Now.AddDays(30), next.DueAt);
        Assert.Equal(Now.AddDays(39), next.GraceEndsAt);
    }

    [Fact]
    public async Task CheckInForOwner_SecondConsecutive_StillAllowedAtLimit()
    {
        Vault vault = SeedOverdueVault(PlanStatus.CheckinOverdue);
        AddClosedPeriod(vault, 1, byExecutor: false);
        AddOpenPeriod(vault, 2, byExecutor: true);

        ExecutorCheckInResponse response = await service.CheckInForOwnerAsync(vault.Id, CancellationToken.None);

        Assert.Equal(2, response.ProxyUsed);
    }

    [Fact]
    public async Task CheckInForOwner_ThirdConsecutive_IsBlocked()
    {
        Vault vault = SeedOverdueVault(PlanStatus.AwaitingCheck);
        AddClosedPeriod(vault, 1, byExecutor: true);
        AddOpenPeriod(vault, 2, byExecutor: true);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CheckInForOwnerAsync(vault.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.CheckinNotAllowed, error.Code);
        Assert.Equal("PROXY_LIMIT_REACHED", error.Extensions["reason"]);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Equal(2, repository.Periods.Count);
    }

    [Fact]
    public async Task CheckInForOwner_OwnerCheckInResetsTheCount()
    {
        Vault vault = SeedOverdueVault(PlanStatus.AwaitingCheck);
        AddClosedPeriod(vault, 1, byExecutor: true);
        AddClosedPeriod(vault, 2, byExecutor: true);
        AddClosedPeriod(vault, 3, byExecutor: false);
        AddOpenPeriod(vault, 4, byExecutor: false);

        ExecutorCheckInResponse response = await service.CheckInForOwnerAsync(vault.Id, CancellationToken.None);

        Assert.Equal(1, response.ProxyUsed);
    }

    [Fact]
    public async Task CheckInForOwner_PlanNotOverdue_IsRejectedBeforeCounting()
    {
        Vault vault = SeedOverdueVault(PlanStatus.Active);
        AddOpenPeriod(vault, 1, byExecutor: false);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CheckInForOwnerAsync(vault.Id, CancellationToken.None));

        Assert.Equal("PLAN_NOT_OVERDUE", error.Extensions["reason"]);
    }
}
