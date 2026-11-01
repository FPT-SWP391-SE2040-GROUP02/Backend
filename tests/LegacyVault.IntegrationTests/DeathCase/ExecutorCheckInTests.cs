using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Executor điểm danh thay Owner qua HTTP trên SQL Server thật (DMS-05, ROLE-03, SEC-04).</summary>
public sealed class ExecutorCheckInTests : IAsyncLifetime
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
    public async Task Dms05_ExecutorCheckInReopensPeriodAndReturnsPlanToActive()
    {
        // Chuẩn bị: Owner quá hạn điểm danh, kỳ 1 còn mở
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-31), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(vault.VaultId, body.GetProperty("vaultId").GetGuid());
        Assert.Equal("ACTIVE", body.GetProperty("planStatus").GetString());
        DateTimeOffset checkedInAt = body.GetProperty("checkedInAt").GetDateTimeOffset();
        Assert.True(body.GetProperty("nextCheckInDueAt").GetDateTimeOffset() > checkedInAt);
        Assert.True(body.GetProperty("graceEndsAt").GetDateTimeOffset() > body.GetProperty("nextCheckInDueAt").GetDateTimeOffset());

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(PlanStatus.Active, (await db.Vaults.Include(v => v.Plan).SingleAsync(v => v.Id == vault.VaultId)).Plan.Status);
            List<CheckInPeriod> periods = await db.CheckInPeriods.Where(p => p.PlanId == vault.PlanId).OrderBy(p => p.Sequence).ToListAsync();
            Assert.Equal(2, periods.Count);
            Assert.NotNull(periods[0].CheckedInAt);
            Assert.NotNull(periods[0].ClosedAt);
            Assert.Null(periods[1].ClosedAt);
            AuditLog log = await db.AuditLogs.SingleAsync(a => a.VaultId == vault.VaultId && a.Action == AuditAction.CheckedIn);
            Assert.Equal(AuditActorRole.Executor, log.ActorRole);
            Assert.Equal(executor.User.Id, log.ActorUserId);
        });
    }

    [SqlFact]
    public async Task Dms05_CheckInIsRejectedWhenOwnerIsNotOverdue()
    {
        // Chuẩn bị: kế hoạch đang ACTIVE
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", null);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("PLAN_NOT_OVERDUE", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Dms05_CheckInIsRejectedWhenDeathCaseIsOpen()
    {
        // Chuẩn bị: Owner quá hạn, Executor đã mở hồ sơ chứng tử
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        HttpResponseMessage opened = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/death-case", null);
        Assert.True(opened.IsSuccessStatusCode);

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", null);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("DEATH_CASE_OPEN", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec04_OwnerCannotUseExecutorCheckIn()
    {
        // Chuẩn bị: Owner quá hạn tự gọi endpoint của Executor
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);

        // Hành động
        HttpResponseMessage response = await vault.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", null);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }
}
