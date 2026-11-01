using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>
/// Executor điểm danh thay chủ kho qua HTTP trên SQL Server thật.
/// Chỉ chạy khi có biến môi trường LEGACYVAULT_TEST_CONNECTION; mỗi lớp test dùng một database tạm.
/// </summary>
public sealed class ExecutorCheckInFlowTests : IAsyncLifetime
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

    private static string CheckInUrl(Guid vaultId)
    {
        return "/api/v1/executor/vaults/" + vaultId + "/check-in";
    }

    /// <summary>Kho có kỳ điểm danh đang mở đã quá hạn, kế hoạch ở trạng thái cho trước, và một Executor đang hiệu lực.</summary>
    private async Task<(DeathCaseTestData.OwnerVault Vault, DeathCaseTestData.ExecutorClient Executor)> CreateOverdueVaultAsync(
        PlanStatus planStatus)
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-40), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, planStatus);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        return (vault, executor);
    }

    [SqlFact]
    public async Task AwaitingCheck_ExecutorChecksIn_OpensNewPeriodWithoutOwnerCheckInTime()
    {
        (DeathCaseTestData.OwnerVault vault, DeathCaseTestData.ExecutorClient executor) =
            await CreateOverdueVaultAsync(PlanStatus.AwaitingCheck);

        HttpResponseMessage response = await executor.Client.PostAsync(CheckInUrl(vault.VaultId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(vault.VaultId, body.GetProperty("vaultId").GetGuid());
        Assert.Equal("ACTIVE", body.GetProperty("planStatus").GetString());
        DateTimeOffset checkedInAt = body.GetProperty("checkedInAt").GetDateTimeOffset();
        Assert.True(body.GetProperty("nextCheckInDueAt").GetDateTimeOffset() > checkedInAt);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.Id == vault.PlanId);
            Assert.Equal(PlanStatus.Active, plan.Status);

            List<CheckInPeriod> periods = await db.CheckInPeriods
                .Where(p => p.PlanId == vault.PlanId).OrderBy(p => p.Sequence).ToListAsync();
            Assert.Equal(2, periods.Count);
            // Kỳ quá hạn đóng lại nhưng không tính là chủ kho đã điểm danh.
            Assert.NotNull(periods[0].ClosedAt);
            Assert.Null(periods[0].CheckedInAt);
            Assert.Null(periods[1].ClosedAt);
            Assert.Equal(2, periods[1].Sequence);
            // Kỳ mới đánh dấu do Executor mở để khóa nút điểm danh của chủ kho trong ngày.
            Assert.False(periods[0].StartedByExecutor);
            Assert.True(periods[1].StartedByExecutor);

            CheckInSchedule? schedule = await db.CheckInSchedules.SingleOrDefaultAsync(s => s.PlanId == vault.PlanId);
            Assert.NotNull(schedule);
            Assert.Null(schedule.LastCheckInAt);

            AuditLog log = await db.AuditLogs.SingleAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.CheckedIn);
            Assert.Equal(AuditActorRole.Executor, log.ActorRole);
            Assert.Equal(executor.User.Id, log.ActorUserId);
        });
    }

    [SqlFact]
    public async Task CheckinOverdue_ExecutorChecksIn_Succeeds()
    {
        (DeathCaseTestData.OwnerVault vault, DeathCaseTestData.ExecutorClient executor) =
            await CreateOverdueVaultAsync(PlanStatus.CheckinOverdue);

        HttpResponseMessage response = await executor.Client.PostAsync(CheckInUrl(vault.VaultId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.Id == vault.PlanId);
            Assert.Equal(PlanStatus.Active, plan.Status);
        });
    }

    [SqlFact]
    public async Task PlanNotOverdue_Returns409WithReason()
    {
        (DeathCaseTestData.OwnerVault vault, DeathCaseTestData.ExecutorClient executor) =
            await CreateOverdueVaultAsync(PlanStatus.Active);

        HttpResponseMessage response = await executor.Client.PostAsync(CheckInUrl(vault.VaultId), null);

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("ACTIVE", problem.GetProperty("planStatus").GetString());
        Assert.Equal("PLAN_NOT_OVERDUE", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task OpenDeathCase_Returns409WithReason()
    {
        (DeathCaseTestData.OwnerVault vault, DeathCaseTestData.ExecutorClient executor) =
            await CreateOverdueVaultAsync(PlanStatus.AwaitingCheck);
        HttpResponseMessage opened = await executor.Client.PostAsync(
            "/api/v1/executor/vaults/" + vault.VaultId + "/death-case", null);
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);

        HttpResponseMessage response = await executor.Client.PostAsync(CheckInUrl(vault.VaultId), null);

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("DEATH_CASE_OPEN", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.Id == vault.PlanId);
            Assert.Equal(PlanStatus.AwaitingCheck, plan.Status);
        });
    }

    [SqlFact]
    public async Task OwnerAndStranger_Return404()
    {
        (DeathCaseTestData.OwnerVault vault, _) = await CreateOverdueVaultAsync(PlanStatus.AwaitingCheck);
        User stranger = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient strangerClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(strangerClient, factory.Services, stranger);

        HttpResponseMessage byOwner = await vault.Client.PostAsync(CheckInUrl(vault.VaultId), null);
        HttpResponseMessage byStranger = await strangerClient.PostAsync(CheckInUrl(vault.VaultId), null);

        await ApiTestHelpers.AssertProblemAsync(byOwner, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(byStranger, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task RevokedExecutor_Returns404()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-40), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.AwaitingCheck);
        User revoked = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await DeathCaseTestData.AddExecutorAsync(factory, vault.VaultId, revoked.Id, ExecutorStatus.Revoked);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, revoked);

        HttpResponseMessage response = await client.PostAsync(CheckInUrl(vault.VaultId), null);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }
}
