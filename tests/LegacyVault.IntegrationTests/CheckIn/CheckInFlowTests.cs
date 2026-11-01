using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>
/// Điểm danh qua HTTP và job nền trên SQL Server thật (docs/api-contract.md mục 6.8).
/// Chỉ chạy khi có biến môi trường LEGACYVAULT_TEST_CONNECTION; mỗi lớp test dùng một database tạm.
/// </summary>
public sealed class CheckInFlowTests : IAsyncLifetime
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

    /// <summary>Tạo user + kho; planStatus khác Draft thì đặt thẳng trong DB (chưa có API kích hoạt kế hoạch).</summary>
    private async Task<(HttpClient Client, Guid VaultId, Guid PlanId)> CreateVaultWithPlanAsync(PlanStatus planStatus)
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid planId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.HandoverPlans.Where(p => p.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, planStatus));
            planId = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Id).SingleAsync();
        });
        return (client, vaultId, planId);
    }

    private static async Task<JsonElement> GetCheckInAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/v1/check-ins/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    [SqlFact]
    public async Task Get_BeforeActivation_ReturnsNotStarted()
    {
        (HttpClient client, _, _) = await CreateVaultWithPlanAsync(PlanStatus.Draft);

        JsonElement body = await GetCheckInAsync(client);

        Assert.Equal("NOT_STARTED", body.GetProperty("status").GetString());
        Assert.Equal("DRAFT", body.GetProperty("planStatus").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCheckInDueAt").ValueKind);
        Assert.Equal(0, body.GetProperty("history").GetProperty("items").GetArrayLength());
        Assert.Equal(30, body.GetProperty("settings").GetProperty("cycleDays").GetInt32());
        Assert.Equal("\"0\"", body.GetProperty("settings").GetProperty("etag").GetString());
    }

    [SqlFact]
    public async Task CheckIn_OnDraftPlan_Returns409NotAllowed()
    {
        (HttpClient client, _, _) = await CreateVaultWithPlanAsync(PlanStatus.Draft);

        HttpResponseMessage response = await client.PostAsync("/api/v1/check-ins", null);

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");
        Assert.Equal("DRAFT", problem.GetProperty("planStatus").GetString());
    }

    [SqlFact]
    public async Task Unauthenticated_Returns401SessionExpired()
    {
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage response = await anonymous.GetAsync("/api/v1/check-ins/me");

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [SqlFact]
    public async Task CheckIn_OnRunningPlan_OpensPeriodAndRepeatIsIdempotent()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);

        HttpResponseMessage first = await client.PostAsync("/api/v1/check-ins", null);
        HttpResponseMessage second = await client.PostAsync("/api/v1/check-ins", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(first);
        JsonElement secondBody = await ApiTestHelpers.ReadJsonAsync(second);
        Assert.Equal("ACTIVE", firstBody.GetProperty("status").GetString());
        Assert.Equal(
            firstBody.GetProperty("nextCheckInDueAt").GetDateTimeOffset(),
            secondBody.GetProperty("nextCheckInDueAt").GetDateTimeOffset());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.CheckInPeriods.CountAsync(p => p.PlanId == planId));
        });
    }

    [SqlFact]
    public async Task WebAndWatchConcurrentCheckIn_OnlyOneNewPeriod()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        HttpResponseMessage seeded = await client.PostAsync("/api/v1/check-ins", null);
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.CheckInSchedules.Where(s => s.PlanId == planId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckInAt, DateTimeOffset.UtcNow.AddDays(-1)));
        });

        Task<HttpResponseMessage> web = client.PostAsync("/api/v1/check-ins", null);
        Task<HttpResponseMessage> watch = client.PostAsJsonAsync("/api/v1/check-ins/smartwatch",
            new { heartRate = 75, isOnWrist = true });
        HttpResponseMessage[] responses = await Task.WhenAll(web, watch);
        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict);
        }
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(2, await db.CheckInPeriods.CountAsync(p => p.PlanId == planId));
            Assert.Equal(1, await db.CheckInPeriods.CountAsync(p => p.PlanId == planId && p.ClosedAt == null));
        });
    }

    [SqlFact]
    public async Task Settings_WhileRunning_StoresPendingAndAppliesOnNextCheckIn()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        await client.PostAsync("/api/v1/check-ins", null);
        JsonElement before = await GetCheckInAsync(client);
        string etag = before.GetProperty("settings").GetProperty("etag").GetString()!;
        DateTimeOffset dueBefore = before.GetProperty("nextCheckInDueAt").GetDateTimeOffset();

        HttpResponseMessage patch = await client.PatchAsJsonAsync(
            "/api/v1/vaults/me/check-in-settings", new { etag, cycleDays = 60, graceDays = 14 });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        JsonElement patched = await ApiTestHelpers.ReadJsonAsync(patch);
        Assert.Equal(30, patched.GetProperty("settings").GetProperty("cycleDays").GetInt32());
        Assert.Equal(60, patched.GetProperty("settings").GetProperty("pendingCycleDays").GetInt32());
        Assert.Equal(dueBefore, patched.GetProperty("nextCheckInDueAt").GetDateTimeOffset());

        // Lùi lần điểm danh về hôm trước để kỳ tiếp theo được áp dụng cấu hình mới.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.CheckInSchedules.Where(s => s.PlanId == planId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckInAt, DateTimeOffset.UtcNow.AddDays(-1)));
        });
        HttpResponseMessage second = await client.PostAsync("/api/v1/check-ins", null);

        JsonElement after = await ApiTestHelpers.ReadJsonAsync(second);
        Assert.Equal(60, after.GetProperty("settings").GetProperty("cycleDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("settings").GetProperty("pendingCycleDays").ValueKind);
        Assert.Equal(2, after.GetProperty("history").GetProperty("totalCount").GetInt32());
        TimeSpan cycle = after.GetProperty("nextCheckInDueAt").GetDateTimeOffset() - DateTimeOffset.UtcNow;
        Assert.InRange(cycle.TotalDays, 59.9, 60.1);
    }

    [SqlFact]
    public async Task Settings_StaleEtag_Returns409EditConflict()
    {
        (HttpClient client, _, _) = await CreateVaultWithPlanAsync(PlanStatus.Draft);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            "/api/v1/vaults/me/check-in-settings", new { etag = "\"999\"", cycleDays = 60 });

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EDIT_CONFLICT");
    }

    [SqlFact]
    public async Task Settings_InvalidCycleDays_Returns400ValidationError()
    {
        (HttpClient client, _, _) = await CreateVaultWithPlanAsync(PlanStatus.Draft);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            "/api/v1/vaults/me/check-in-settings", new { etag = "\"0\"", cycleDays = 45 });

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Settings_MissingEtag_Returns400ValidationError()
    {
        (HttpClient client, _, _) = await CreateVaultWithPlanAsync(PlanStatus.Draft);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            "/api/v1/vaults/me/check-in-settings", new { cycleDays = 60 });

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Job_AfterDue_MovesToOverdueAndRecordsReminderOnlyOnce()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        await client.PostAsync("/api/v1/check-ins", null);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await SetPeriodDatesAsync(planId, now.AddDays(-30).AddHours(-1), now.AddHours(-1), now.AddDays(7).AddHours(-1));

        Guid periodId = await FindOpenPeriodIdAsync(planId);
        await RunJobAsync(periodId);
        await RunJobAsync(periodId);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanStatus.CheckinOverdue, plan.Status);
            CheckInPeriod period = await db.CheckInPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
            Assert.NotNull(period.OverdueAt);
            Assert.Null(period.AwaitingCheckAt);
            List<CheckInReminder> reminders = await db.CheckInReminders.Where(r => r.PeriodId == periodId).ToListAsync();
            // Job mở lần đầu sau hạn: mốc mới nhất (GraceStarted) được gửi một lần, hai mốc cũ bị bỏ qua.
            CheckInReminder sent = Assert.Single(reminders, r => r.Status == CheckInReminderStatus.Sent);
            Assert.Equal(CheckInReminderKind.GraceStarted, sent.Kind);
            Assert.Equal(2, reminders.Count(r => r.Status == CheckInReminderStatus.Skipped));
            Assert.Equal(3, reminders.Count);
        });
        JsonElement body = await GetCheckInAsync(client);
        Assert.Equal("CHECKIN_OVERDUE", body.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Job_LongDowntime_JumpsToAwaitingCheck()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        await client.PostAsync("/api/v1/check-ins", null);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await SetPeriodDatesAsync(planId, now.AddDays(-50), now.AddDays(-20), now.AddDays(-13));

        Guid periodId = await FindOpenPeriodIdAsync(planId);
        await RunJobAsync(periodId);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanStatus.AwaitingCheck, plan.Status);
            CheckInPeriod period = await db.CheckInPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
            Assert.NotNull(period.AwaitingCheckAt);
            Assert.False(await db.CheckInReminders.AnyAsync(r => r.PeriodId == periodId));
        });
        JsonElement body = await GetCheckInAsync(client);
        Assert.Equal("AWAITING_CHECK", body.GetProperty("status").GetString());

        // Owner điểm danh từ AWAITING_CHECK → về ACTIVE (SRS §11.2).
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.CheckInSchedules.Where(s => s.PlanId == planId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckInAt, DateTimeOffset.UtcNow.AddDays(-1)));
        });
        HttpResponseMessage checkIn = await client.PostAsync("/api/v1/check-ins", null);
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(checkIn);
        Assert.Equal("ACTIVE", after.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task RemovingLastReadyContainer_StopsTheCheckInSchedule()
    {
        // Chuẩn bị: kế hoạch ACTIVE có kỳ điểm danh đang mở và một gói READY (dựng thẳng trong DB, chưa có chỉ định người nhận).
        (HttpClient client, Guid vaultId, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        await client.PostAsync("/api/v1/check-ins", null);
        HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/containers", new { name = "Gói sẵn sàng" });
        Guid containerId = (await ApiTestHelpers.ReadJsonAsync(created)).GetProperty("id").GetGuid();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Containers.Where(c => c.Id == containerId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContainerStatus.Ready));
        });

        // Hành động: xóa gói READY cuối cùng và xác nhận kế hoạch về nháp (SETUP-16).
        HttpResponseMessage removedContainer = await client.DeleteAsync(
            $"/api/v1/vaults/{vaultId}/containers/{containerId}?confirmPlanDeactivation=true");

        // Kiểm tra: kế hoạch về DRAFT, kỳ điểm danh đóng nhưng không bị coi là đã điểm danh (SETUP-16: dừng lịch).
        Assert.Equal(HttpStatusCode.OK, removedContainer.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanStatus.Draft, plan.Status);
            CheckInPeriod period = await db.CheckInPeriods.AsNoTracking().SingleAsync(p => p.PlanId == planId);
            Assert.NotNull(period.ClosedAt);
            Assert.Null(period.CheckedInAt);
        });
        JsonElement body = await GetCheckInAsync(client);
        Assert.Equal("NOT_STARTED", body.GetProperty("status").GetString());
        // Lịch sử trả closedAt để FE hiện kỳ này là "Đã dừng", không phải "Đang chạy".
        JsonElement stopped = body.GetProperty("history").GetProperty("items")[0];
        Assert.Equal(JsonValueKind.String, stopped.GetProperty("closedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("checkedInAt").ValueKind);
    }

    [SqlFact]
    public async Task Database_RejectsSecondOpenPeriodForSamePlan()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        await client.PostAsync("/api/v1/check-ins", null);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            CheckInPeriod duplicate = new CheckInPeriod();
            duplicate.PlanId = planId;
            duplicate.Sequence = 99;
            duplicate.StartedAt = DateTimeOffset.UtcNow;
            duplicate.DueAt = DateTimeOffset.UtcNow.AddDays(30);
            duplicate.GraceEndsAt = DateTimeOffset.UtcNow.AddDays(37);
            db.CheckInPeriods.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [SqlFact]
    public async Task Database_RowVersionStopsTwoWritersOnSamePeriod()
    {
        (HttpClient client, _, Guid planId) = await CreateVaultWithPlanAsync(PlanStatus.Active);
        await client.PostAsync("/api/v1/check-ins", null);
        Guid periodId = await FindOpenPeriodIdAsync(planId);

        using (IServiceScope scopeA = factory.Services.CreateScope())
        using (IServiceScope scopeB = factory.Services.CreateScope())
        {
            AppDbContext dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
            AppDbContext dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
            CheckInPeriod periodA = await dbA.CheckInPeriods.SingleAsync(p => p.Id == periodId);
            CheckInPeriod periodB = await dbB.CheckInPeriods.SingleAsync(p => p.Id == periodId);

            periodA.OverdueAt = DateTimeOffset.UtcNow;
            await dbA.SaveChangesAsync();
            periodB.CheckedInAt = DateTimeOffset.UtcNow;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
        }
    }

    private async Task SetPeriodDatesAsync(Guid planId, DateTimeOffset startedAt, DateTimeOffset dueAt, DateTimeOffset graceEndsAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.CheckInPeriods.Where(p => p.PlanId == planId && p.ClosedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StartedAt, startedAt)
                    .SetProperty(p => p.DueAt, dueAt)
                    .SetProperty(p => p.GraceEndsAt, graceEndsAt));
        });
    }

    private async Task<Guid> FindOpenPeriodIdAsync(Guid planId)
    {
        Guid periodId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            periodId = await db.CheckInPeriods
                .Where(p => p.PlanId == planId && p.ClosedAt == null)
                .Select(p => p.Id)
                .SingleAsync();
        });
        return periodId;
    }

    private async Task RunJobAsync(Guid periodId)
    {
        // Mỗi lần chạy dùng một scope mới, giống CheckInMonitor.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ICheckInJob job = scope.ServiceProvider.GetRequiredService<ICheckInJob>();
            await job.ProcessPeriodAsync(periodId, CancellationToken.None);
        }
    }
}
