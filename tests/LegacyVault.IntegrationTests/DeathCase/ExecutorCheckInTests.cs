using System.Net;
using System.Net.Http.Json;
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
        HttpResponseMessage response = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", ApiTestHelpers.ProxyCheckInBody());

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
            // Chủ kho không tự điểm danh nên kỳ cũ chỉ có ClosedAt (kỳ bị dừng), không có CheckedInAt.
            Assert.Null(periods[0].CheckedInAt);
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
        HttpResponseMessage response = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", ApiTestHelpers.ProxyCheckInBody());

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
        HttpResponseMessage response = await executor.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", ApiTestHelpers.ProxyCheckInBody());

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("DEATH_CASE_OPEN", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task ProxyCheckIn_StoresContactAndLogsOnlyTheMethodCode()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-31), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        HttpResponseMessage response = await executor.Client.PostAsync(
            $"/api/v1/executor/vaults/{vault.VaultId}/check-in", ApiTestHelpers.ProxyCheckInBody("IN_PERSON"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(3, body.GetProperty("proxyCheckInLimit").GetInt32());
        Assert.Equal(2, body.GetProperty("proxyCheckInsLeft").GetInt32());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            CheckInPeriod latest = await db.CheckInPeriods.Where(p => p.PlanId == vault.PlanId)
                .OrderByDescending(p => p.Sequence).FirstAsync();
            Assert.True(latest.StartedByExecutor);
            Assert.Equal("Chị Lan (vợ)", latest.ProxyContactedWho);
            Assert.Equal("IN_PERSON", latest.ProxyContactMethod);
            Assert.NotNull(latest.ProxyContactedAt);
            AuditLog log = await db.AuditLogs.SingleAsync(a => a.VaultId == vault.VaultId && a.Action == AuditAction.CheckedIn);
            // Chủ kho đọc được nhật ký: chỉ ghi mã cách liên lạc, không ghi tên người đã liên lạc.
            Assert.Contains("IN_PERSON", log.Reason);
            Assert.DoesNotContain("Lan", log.Reason);
        });
    }

    [SqlFact]
    public async Task ProxyCheckIn_IsRejectedAfterTheLimitOfConsecutiveProxies()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-31), null);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        string url = $"/api/v1/executor/vaults/{vault.VaultId}/check-in";

        for (int i = 0; i < 3; i++)
        {
            await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
            HttpResponseMessage ok = await executor.Client.PostAsync(url, ApiTestHelpers.ProxyCheckInBody());
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        HttpResponseMessage fourth = await executor.Client.PostAsync(url, ApiTestHelpers.ProxyCheckInBody());
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(fourth, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("PROXY_LIMIT_REACHED", problem.GetProperty("reason").GetString());

        JsonElement vaults = await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync("/api/v1/executor/vaults"));
        JsonElement mine = vaults[0];
        Assert.Equal(3, mine.GetProperty("proxyCheckInLimit").GetInt32());
        Assert.Equal(3, mine.GetProperty("proxyCheckInsUsed").GetInt32());
        Assert.Equal(0, mine.GetProperty("proxyCheckInsLeft").GetInt32());
    }

    [SqlFact]
    public async Task ProxyCheckIn_CounterResetsWhenTheOwnerChecksInHimself()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-31), null);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        string url = $"/api/v1/executor/vaults/{vault.VaultId}/check-in";
        for (int i = 0; i < 3; i++)
        {
            await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
            Assert.Equal(HttpStatusCode.OK, (await executor.Client.PostAsync(url, ApiTestHelpers.ProxyCheckInBody())).StatusCode);
        }

        // Chủ kho tự điểm danh: kỳ mới nhất do chủ kho mở, không còn là kỳ do Executor mở.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            CheckInPeriod latest = await db.CheckInPeriods.Where(p => p.PlanId == vault.PlanId)
                .OrderByDescending(p => p.Sequence).FirstAsync();
            latest.StartedByExecutor = false;
            await db.SaveChangesAsync();
        });
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);

        HttpResponseMessage again = await executor.Client.PostAsync(url, ApiTestHelpers.ProxyCheckInBody());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(again);
        Assert.Equal(2, body.GetProperty("proxyCheckInsLeft").GetInt32());
    }

    [SqlFact]
    public async Task ProxyCheckIn_RejectsMissingOrInvalidContact()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-31), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        string url = $"/api/v1/executor/vaults/{vault.VaultId}/check-in";

        HttpResponseMessage emptyBody = await executor.Client.PostAsync(url, JsonContent.Create(new { }));
        HttpResponseMessage badMethod = await executor.Client.PostAsync(url, ApiTestHelpers.ProxyCheckInBody("CARRIER_PIGEON"));
        HttpResponseMessage future = await executor.Client.PostAsync(
            url, ApiTestHelpers.ProxyCheckInBody("PHONE", DateTimeOffset.UtcNow.AddDays(2)));

        Assert.Equal(HttpStatusCode.BadRequest, emptyBody.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badMethod.StatusCode);
        await ApiTestHelpers.AssertProblemAsync(future, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        // Không lần nào làm đổi kế hoạch hay mở kỳ mới.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.CheckInPeriods.CountAsync(p => p.PlanId == vault.PlanId));
        });
    }

    [SqlFact]
    public async Task ExecutorVaultList_ShowsOwnerCheckInButNotTheExecutorProxyCheckIn()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Chưa ai điểm danh: không có giờ điểm danh.
        JsonElement before = (await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync("/api/v1/executor/vaults")))[0];
        Assert.Equal(JsonValueKind.Null, before.GetProperty("lastOwnerCheckInAt").ValueKind);

        // Chủ kho tự điểm danh: Executor thấy giờ đó.
        HttpResponseMessage ownerCheckIn = await vault.Client.PostAsync("/api/v1/check-ins", null);
        Assert.Equal(HttpStatusCode.OK, ownerCheckIn.StatusCode);
        JsonElement after = (await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync("/api/v1/executor/vaults")))[0];
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("lastOwnerCheckInAt").ValueKind);
        Assert.True(after.GetProperty("lastOwnerCheckInAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [SqlFact]
    public async Task ExecutorVaultList_ProxyCheckInDoesNotCountAsOwnerCheckIn()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-31), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        HttpResponseMessage proxy = await executor.Client.PostAsync(
            $"/api/v1/executor/vaults/{vault.VaultId}/check-in", ApiTestHelpers.ProxyCheckInBody());
        Assert.Equal(HttpStatusCode.OK, proxy.StatusCode);

        JsonElement mine = (await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync("/api/v1/executor/vaults")))[0];
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("lastOwnerCheckInAt").ValueKind);
    }

    [SqlFact]
    public async Task Sec04_OwnerCannotUseExecutorCheckIn()
    {
        // Chuẩn bị: Owner quá hạn tự gọi endpoint của Executor
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.CheckinOverdue);

        // Hành động
        HttpResponseMessage response = await vault.Client.PostAsync($"/api/v1/executor/vaults/{vault.VaultId}/check-in", ApiTestHelpers.ProxyCheckInBody());

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }
}
