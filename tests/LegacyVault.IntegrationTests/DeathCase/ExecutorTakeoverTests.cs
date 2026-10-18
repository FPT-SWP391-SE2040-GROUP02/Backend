using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Executor dự phòng tiếp quản khi Executor chính bị khóa (ASSIGN-02, ASSIGN-05, ASSIGN-03 phía F2B, AC-60)
/// trên SQL Server thật.
/// </summary>
public sealed class ExecutorTakeoverTests : IAsyncLifetime
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
    public async Task Assign02_GetAvailable_SkipsLockedAndDisabled_PrimaryFirst()
    {
        // Chuẩn bị: Executor chính bị khóa, dự phòng A còn hoạt động, dự phòng B bị vô hiệu hóa
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User primary = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Locked);
        User backupA = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User backupB = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Disabled);
        await DeathCaseTestData.AddAcceptedExecutorAsync(factory, vault.VaultId, backupA.Id, ExecutorRole.Backup);
        await DeathCaseTestData.AddAcceptedExecutorAsync(factory, vault.VaultId, backupB.Id, ExecutorRole.Backup);
        await DeathCaseTestData.AddAcceptedExecutorAsync(factory, vault.VaultId, primary.Id, ExecutorRole.Primary);

        // Hành động
        List<Executor> available;
        List<Executor> active;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IExecutorReader executorReader = scope.ServiceProvider.GetRequiredService<IExecutorReader>();
            available = await executorReader.GetAvailableAsync(vault.VaultId, CancellationToken.None);
            active = await executorReader.GetActiveAsync(vault.VaultId, CancellationToken.None);
        }

        // Kiểm tra: chỉ còn dự phòng A; danh sách hiệu lực (ROLE-03) vẫn đủ 3 người, Executor chính đứng đầu
        Executor onlyAvailable = Assert.Single(available);
        Assert.Equal(backupA.Id, onlyAvailable.UserId);
        Assert.Equal(3, active.Count);
        Assert.Equal(primary.Id, active[0].UserId);
        Assert.Equal(ExecutorRole.Primary, active[0].Role);
    }

    [SqlFact]
    public async Task Assign02_LockedPrimary_CheckInIncidentGoesToBackup()
    {
        // Chuẩn bị: Executor chính bị khóa, dự phòng còn hoạt động; kỳ điểm danh đang chờ kiểm
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient primary = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCaseTestData.ExecutorClient backup = await DeathCaseTestData.CreateExecutorClientAsync(
            factory, vault.VaultId, ExecutorRole.Backup);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, primary.User.Id, UserStatus.Locked);
        Guid periodId = await MakePeriodAwaitingAsync(vault);

        // Hành động
        await RecordPeriodAsync(periodId);

        // Kiểm tra: sự cố CHECK_IN_OVERDUE giao cho Executor dự phòng
        List<Incident> overdue = await LoadIncidentsAsync(IncidentType.CheckInOverdue, periodId);
        Incident incident = Assert.Single(overdue);
        Assert.Equal(backup.User.Id, incident.AssigneeUserId);
        List<Incident> stuck = await LoadIncidentsAsync(IncidentType.NoExecutor, periodId);
        Assert.Empty(stuck);
    }

    [SqlFact]
    public async Task Assign05_AllExecutorsLocked_StuckIncident()
    {
        // Chuẩn bị: cả Executor chính và dự phòng đều bị khóa
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient primary = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCaseTestData.ExecutorClient backup = await DeathCaseTestData.CreateExecutorClientAsync(
            factory, vault.VaultId, ExecutorRole.Backup);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, primary.User.Id, UserStatus.Locked);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, backup.User.Id, UserStatus.Locked);
        Guid periodId = await MakePeriodAwaitingAsync(vault);

        // Hành động
        await RecordPeriodAsync(periodId);

        // Kiểm tra: sự cố "bị kẹt" NO_EXECUTOR, không có CHECK_IN_OVERDUE
        List<Incident> stuck = await LoadIncidentsAsync(IncidentType.NoExecutor, periodId);
        Assert.Single(stuck);
        List<Incident> overdue = await LoadIncidentsAsync(IncidentType.CheckInOverdue, periodId);
        Assert.Empty(overdue);
    }

    [SqlFact]
    public async Task Assign02_AC60_BackupSubmitsSupplementAfterPrimaryLocked()
    {
        // Chuẩn bị: Executor chính đã gửi hồ sơ, Verifier yêu cầu bổ sung, rồi Executor chính bị khóa
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        DeathCaseTestData.ExecutorClient backup = await DeathCaseTestData.CreateExecutorClientAsync(
            factory, submitted.Vault.VaultId, ExecutorRole.Backup);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Thiếu nơi cấp", new[] { "ISSUED_BY" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Executor.User.Id, UserStatus.Locked);

        // Hành động: Executor dự phòng tạo bản 2, sửa nơi cấp rồi gửi
        HttpResponseMessage current = await backup.Client.GetAsync(DeathCaseApi.CaseUrl(submitted.CaseId));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        JsonElement currentBody = await ApiTestHelpers.ReadJsonAsync(current);
        HttpResponseMessage created = await backup.Client.PostAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/versions",
            new { etag = currentBody.GetProperty("etag").GetString() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(created);
        HttpResponseMessage edit = await backup.Client.PutAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/draft",
            new { etag = draft.GetProperty("etag").GetString(), issuedBy = "UBND phường Bến Thành" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        JsonElement edited = await ApiTestHelpers.ReadJsonAsync(edit);
        JsonElement resubmitted = await DeathCaseApi.SubmitOkAsync(backup.Client, edited);

        // Kiểm tra: bản 2 của dự phòng, bản 1 vẫn của Executor chính (ASSIGN-03 phía F2B)
        Assert.Equal("UNDER_REVIEW", resubmitted.GetProperty("status").GetString());
        List<DeathCaseVersion> versions = await LoadVersionsAsync(submitted.CaseId);
        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[0].VersionNo);
        Assert.Equal(submitted.Executor.User.Id, versions[0].CreatedByUserId);
        Assert.Equal(submitted.Executor.User.Id, versions[0].SubmittedByUserId);
        Assert.Equal(2, versions[1].VersionNo);
        Assert.Equal(backup.User.Id, versions[1].CreatedByUserId);
        Assert.Equal(backup.User.Id, versions[1].SubmittedByUserId);
    }

    /// <summary>Kỳ điểm danh quá hạn chờ kiểm (giống MakePeriodAwaitingAsync trong AdminOperationsTests).</summary>
    private async Task<Guid> MakePeriodAwaitingAsync(DeathCaseTestData.OwnerVault vault)
    {
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-60), null);
        Guid periodId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            periodId = await db.CheckInPeriods.Where(p => p.PlanId == vault.PlanId).Select(p => p.Id).SingleAsync();
            await db.CheckInPeriods.Where(p => p.Id == periodId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.AwaitingCheckAt, DateTimeOffset.UtcNow.AddDays(-1)));
        });
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.AwaitingCheck);
        return periodId;
    }

    private async Task RecordPeriodAsync(Guid periodId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>()
                .RecordCheckInPeriodAsync(periodId, CancellationToken.None);
        }
    }

    private async Task<List<Incident>> LoadIncidentsAsync(IncidentType type, Guid periodId)
    {
        List<Incident> incidents = new List<Incident>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            incidents = await db.Set<Incident>().AsNoTracking()
                .Where(i => i.Type == type && i.TargetId == periodId)
                .ToListAsync();
        });
        return incidents;
    }

    private async Task<List<DeathCaseVersion>> LoadVersionsAsync(Guid caseId)
    {
        List<DeathCaseVersion> versions = new List<DeathCaseVersion>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            versions = await db.Set<DeathCaseVersion>().AsNoTracking()
                .Where(v => v.CaseId == caseId)
                .OrderBy(v => v.VersionNo)
                .ToListAsync();
        });
        return versions;
    }
}
