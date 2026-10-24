using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>Nhật ký điểm danh và đổi lịch điểm danh (SEC-13, DMS-01) bằng repository giả, không cần DB.</summary>
public sealed class AuditCheckInServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodStart = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeCheckInRepository repository = new FakeCheckInRepository();
    private readonly FakeAuditLogRepository auditLogs = new FakeAuditLogRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly CheckInService service;
    private readonly Guid ownerId = Guid.NewGuid();

    public AuditCheckInServiceTests()
    {
        currentUser.UserId = ownerId;
        service = new CheckInService(currentUser, vaults, repository, auditLogs, unitOfWork, clock);
    }

    /// <summary>Kho có kế hoạch đang chạy và kỳ 1 đang mở (chu kỳ 30, chờ 7).</summary>
    private Vault SeedRunningVault()
    {
        Vault vault = CheckInTestData.CreateVault(ownerId, PlanStatus.Active);
        vaults.Vaults.Add(vault);
        repository.Periods.Add(CheckInTestData.CreateOpenPeriod(vault.Plan, 1, PeriodStart, 30, 7));
        return vault;
    }

    [Fact]
    public async Task Sec13_CheckIn_LogsNewPeriod()
    {
        Vault vault = SeedRunningVault();

        await service.CheckInAsync(CancellationToken.None);

        AuditLog log = Assert.Single(auditLogs.Logs);
        CheckInPeriod opened = repository.Periods[repository.Periods.Count - 1];
        Assert.Equal(AuditAction.CheckedIn, log.Action);
        Assert.Equal(AuditTargetType.CheckIn, log.TargetType);
        Assert.Equal(opened.Id, log.TargetId);
        Assert.Equal(2, log.TargetVersion);
        Assert.Equal(vault.Id, log.VaultId);
        Assert.Equal(ownerId, log.ActorUserId);
        Assert.Equal(Now, log.At);
    }

    [Fact]
    public async Task Dms02_RepeatedCheckIn_LogsOnce()
    {
        SeedRunningVault();

        await service.CheckInAsync(CancellationToken.None);
        clock.Now = Now.AddHours(6);
        await service.CheckInAsync(CancellationToken.None);

        Assert.Single(auditLogs.Logs);
    }

    [Fact]
    public async Task Dms01_SettingsDuringOpenPeriod_LogsAppliesNextPeriod()
    {
        SeedRunningVault();
        UpdateCheckInSettingsRequest request = new UpdateCheckInSettingsRequest();
        // Chưa có lịch điểm danh: etag của bản "chưa sửa lần nào" (Etag.From(null)).
        request.Etag = Etag.From(null);
        request.CycleDays = 60;
        request.GraceDays = 14;

        await service.UpdateSettingsAsync(request, CancellationToken.None);

        AuditLog log = Assert.Single(auditLogs.Logs);
        Assert.Equal(AuditAction.CheckInSettingsUpdated, log.Action);
        Assert.Equal("Chu kỳ 60 ngày, chờ 14 ngày; áp dụng từ kỳ sau", log.Reason);
    }
}
