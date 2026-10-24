using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Tắt job lưu dữ liệu trong mọi test dùng API thật; test tự gọi IRetentionJob.</summary>
internal static class RetentionTestEnvironment
{
    [ModuleInitializer]
    internal static void DisableRetentionJob()
    {
        Environment.SetEnvironmentVariable("Retention__Enabled", "false");
    }
}

public sealed class CapturingRetentionSender : IRetentionNoticeSender
{
    public ConcurrentQueue<RetentionNotice> Notices { get; } = new ConcurrentQueue<RetentionNotice>();

    public Task SendAsync(RetentionNotice notice, CancellationToken ct)
    {
        Notices.Enqueue(notice);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Lưu và xóa dữ liệu sau bàn giao trên SQL Server thật (SRS §7.7, RETENTION-01..09, SETUP-18).
/// Thời gian được giả lập bằng cách lùi các mốc trong DB.
/// </summary>
public sealed class RetentionFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private WebApplicationFactory<Program> app = null!;
    private readonly CapturingHandoverSender invitations = new CapturingHandoverSender();
    private readonly CapturingRetentionSender notices = new CapturingRetentionSender();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
        app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHandoverInvitationSender>();
                services.AddSingleton<IHandoverInvitationSender>(invitations);
                services.RemoveAll<IRetentionNoticeSender>();
                services.AddSingleton<IRetentionNoticeSender>(notices);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await app.DisposeAsync();
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private sealed class Scenario
    {
        public Guid VaultId { get; set; }
        public Guid PlanId { get; set; }
        public Guid ExtraAssetId { get; set; }
        public List<string> Tokens { get; set; } = new List<string>();
    }

    private HttpClient ClientFor(User user)
    {
        HttpClient client = app.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, app.Services, user);
        return client;
    }

    /// <summary>
    /// Kho có một gói READY với một tài sản, chỉ định cho beneficiaryCount người nhận; thêm một tài sản ngoài gói.
    /// Hồ sơ chứng tử được duyệt nên F3 đã mở, mỗi người nhận một lượt.
    /// </summary>
    private async Task<Scenario> BuildHandoverAsync(int beneficiaryCount)
    {
        DeathCaseTestData.OwnerVault owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        HttpClient ownerClient = ClientFor(owner.Owner);
        Guid vaultId = owner.VaultId;
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(ownerClient, vaultId, "Ghi chú");
        Guid extraAssetId = await ApiTestHelpers.CreateTextAssetAsync(ownerClient, vaultId, "Ngoài gói");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(ownerClient, vaultId, "Gói")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(ownerClient, vaultId, containerId, assetId)).StatusCode);
        string[] ids = { "001090000001", "001090000002" };
        for (int i = 0; i < beneficiaryCount; i++)
        {
            JsonElement b = await ApiTestHelpers.CreateBeneficiaryAsync(ownerClient, vaultId, ids[i], "Người nhận " + i);
            Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(
                ownerClient, vaultId, containerId, b.GetProperty("id").GetGuid())).StatusCode);
        }

        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            ClientFor(executor.User), vaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(ClientFor(executor.User), prepared);
        Guid caseId = submitted.GetProperty("id").GetGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseNoticeJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseNoticeJob>();
            foreach (Guid id in await job.FindPendingNoticeIdsAsync(CancellationToken.None))
            {
                await job.ProcessNoticeAsync(id, CancellationToken.None);
            }
        }
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == caseId).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ObjectionStartsAt, DateTimeOffset.UtcNow.AddHours(-80))
                .SetProperty(c => c.ObjectionEndsAt, DateTimeOffset.UtcNow.AddHours(-8)));
        });
        HttpClient verifierClient = ClientFor(verifier);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(verifierClient, caseId);
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveAsync(
            verifierClient, caseId, detail.GetProperty("etag").GetString(), "Đã đối chiếu hồ sơ.");
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        Scenario scenario = new Scenario();
        scenario.VaultId = vaultId;
        scenario.PlanId = owner.PlanId;
        scenario.ExtraAssetId = extraAssetId;
        foreach (HandoverInvitation invitation in invitations.Invitations)
        {
            scenario.Tokens.Add(invitation.Token);
        }
        return scenario;
    }

    private async Task<RetentionRunResult> RunAsync(Guid planId)
    {
        using (IServiceScope scope = app.Services.CreateScope())
        {
            return await scope.ServiceProvider.GetRequiredService<IRetentionJob>().ProcessPlanAsync(planId, CancellationToken.None);
        }
    }

    private async Task<List<HandoverTurn>> TurnsAsync(Guid planId)
    {
        List<HandoverTurn> turns = new List<HandoverTurn>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            turns = await db.Set<HandoverTurn>().Include(t => t.Grant).Where(t => t.PlanId == planId)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync();
        });
        return turns;
    }

    private Task PassResponseDeadlineAsync(Guid planId)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<HandoverTurn>().Where(t => t.PlanId == planId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ResponseDeadline, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
    }

    private Task PassRetentionDeadlineAsync(Guid turnId)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<HandoverTurn>().Where(t => t.Id == turnId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RetentionDeadline, DateTimeOffset.UtcNow.AddSeconds(-1)));
        });
    }

    [SqlFact]
    public async Task Retention01_ClockStartsOnlyAfterResponseWindow_NoticeSent()
    {
        Scenario s = await BuildHandoverAsync(1);

        await RunAsync(s.PlanId);
        Assert.Equal(RetentionStatus.NotStarted, Assert.Single(await TurnsAsync(s.PlanId)).RetentionStatus);

        await PassResponseDeadlineAsync(s.PlanId);
        RetentionRunResult result = await RunAsync(s.PlanId);

        HandoverTurn turn = Assert.Single(await TurnsAsync(s.PlanId));
        Assert.Equal(RetentionStatus.RetentionActive, turn.RetentionStatus);
        Assert.NotNull(turn.RetentionStartedAt);
        Assert.True(turn.RetentionDeadline > DateTimeOffset.UtcNow.AddDays(29));
        Assert.Equal(1, result.NoticesSent);
        Assert.Equal(RetentionRules.NoticeStarted, turn.RetentionNoticeStage);

        // Chạy lặp không gửi lại mốc và không đặt lại hạn
        await RunAsync(s.PlanId);
        Assert.Single(notices.Notices);
        Assert.Equal(turn.RetentionDeadline, Assert.Single(await TurnsAsync(s.PlanId)).RetentionDeadline);
    }

    [SqlFact]
    public async Task Retention04_ClaimPausesClock_DeclineResumesRemainingTime()
    {
        Scenario s = await BuildHandoverAsync(1);
        await PassResponseDeadlineAsync(s.PlanId);
        await RunAsync(s.PlanId);
        Guid turnId = Assert.Single(await TurnsAsync(s.PlanId)).Id;
        // Đồng hồ đã chạy được 20 ngày: còn 10 ngày
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<HandoverTurn>().Where(t => t.Id == turnId)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.RetentionDeadline, DateTimeOffset.UtcNow.AddDays(10)));
        });

        // Người nhận nhận lượt (bypass xác minh ở Development): claim đang xử lý → tạm dừng
        User recipient = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = ClientFor(recipient);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/handover/invitations/{s.Tokens[0]}/claim", null)).StatusCode);
        await RunAsync(s.PlanId);
        HandoverTurn held = Assert.Single(await TurnsAsync(s.PlanId));
        Assert.Equal(RetentionStatus.DeletionHeld, held.RetentionStatus);
        Assert.InRange(held.RetentionRemainingSeconds!.Value, (long)TimeSpan.FromDays(10).TotalSeconds - 120, (long)TimeSpan.FromDays(10).TotalSeconds);

        // Từ chối: chạy tiếp phần còn lại, không cộng thêm 30 ngày
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/handover/turns/{turnId}/decline", null)).StatusCode);
        await RunAsync(s.PlanId);
        HandoverTurn resumed = Assert.Single(await TurnsAsync(s.PlanId));
        Assert.Equal(RetentionStatus.RetentionActive, resumed.RetentionStatus);
        Assert.True(resumed.RetentionDeadline < DateTimeOffset.UtcNow.AddDays(10).AddMinutes(1));
        Assert.True(resumed.RetentionDeadline > DateTimeOffset.UtcNow.AddDays(9));
    }

    [SqlFact]
    public async Task Retention06_DueTurnIsDeleted_LinkInvalid_ContentShredded_VaultClosed()
    {
        Scenario s = await BuildHandoverAsync(1);
        await PassResponseDeadlineAsync(s.PlanId);
        await RunAsync(s.PlanId);
        Guid turnId = Assert.Single(await TurnsAsync(s.PlanId)).Id;
        await PassRetentionDeadlineAsync(turnId);

        RetentionRunResult result = await RunAsync(s.PlanId);

        Assert.Equal(1, result.TurnsDeleted);
        Assert.True(result.VaultClosed);
        HandoverTurn turn = Assert.Single(await TurnsAsync(s.PlanId));
        Assert.Equal(RetentionStatus.Deleted, turn.RetentionStatus);
        Assert.Equal(HandoverTurnStatus.Lapsed, turn.Status);
        Assert.NotNull(turn.DeletedAt);

        // Link cũ không còn dùng được
        HttpResponseMessage link = await app.CreateClient().GetAsync($"/api/v1/handover/invitations/{s.Tokens[0]}");
        await ApiTestHelpers.AssertProblemAsync(link, HttpStatusCode.NotFound, "HANDOVER_INVITATION_INVALID");

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<Asset> assets = await db.Assets.Where(a => a.VaultId == s.VaultId).ToListAsync();
            Assert.Equal(2, assets.Count);
            foreach (Asset asset in assets)
            {
                Assert.NotNull(asset.ContentDeletedAt);
                Assert.NotNull(asset.StorageDeletedAt);
                Assert.All(asset.WrappedDataKey, b => Assert.Equal(0, b));
                Assert.False(File.Exists(Path.Combine(factory.StorageRoot, asset.StorageKey)));
            }
            Vault vault = await db.Vaults.Include(v => v.Plan).SingleAsync(v => v.Id == s.VaultId);
            Assert.Equal(VaultStatus.Closed, vault.Status);
            Assert.Equal(PlanStatus.Closed, vault.Plan.Status);
        });
    }

    [SqlFact]
    public async Task Retention02_SharedVersionKeptWhileAnotherTurnStillHoldsIt()
    {
        Scenario s = await BuildHandoverAsync(2);
        await PassResponseDeadlineAsync(s.PlanId);
        await RunAsync(s.PlanId);
        List<HandoverTurn> turns = await TurnsAsync(s.PlanId);
        Assert.Equal(2, turns.Count);

        // Chỉ lượt đầu tới hạn
        await PassRetentionDeadlineAsync(turns[0].Id);
        RetentionRunResult first = await RunAsync(s.PlanId);

        Assert.Equal(1, first.TurnsDeleted);
        Assert.Equal(0, first.AssetsShredded);
        Assert.False(first.VaultClosed);

        // Lượt thứ hai tới hạn: giờ mới xóa nội dung và đóng kho
        await PassRetentionDeadlineAsync(turns[1].Id);
        RetentionRunResult second = await RunAsync(s.PlanId);
        Assert.Equal(1, second.TurnsDeleted);
        Assert.True(second.VaultClosed);
        Assert.Equal(2, second.AssetsShredded);
    }

    [SqlFact]
    public async Task Retention09_ActiveGrantHolds_ExpiredGrantStartsClock_NoAutoRegrant()
    {
        Scenario s = await BuildHandoverAsync(1);
        User recipient = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = ClientFor(recipient);
        JsonElement claimed = await ApiTestHelpers.ReadJsonAsync(
            await client.PostAsync($"/api/v1/handover/invitations/{s.Tokens[0]}/claim", null));
        Guid turnId = claimed.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/handover/turns/{turnId}/accept", null)).StatusCode);

        await RunAsync(s.PlanId);
        Assert.Equal(RetentionStatus.NotStarted, Assert.Single(await TurnsAsync(s.PlanId)).RetentionStatus);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<HandoverGrant>().Where(g => g.TurnId == turnId)
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        });
        await RunAsync(s.PlanId);

        HandoverTurn turn = Assert.Single(await TurnsAsync(s.PlanId));
        Assert.Equal(RetentionStatus.RetentionActive, turn.RetentionStatus);
        Assert.Equal(HandoverTurnStatus.HandoverCommitted, turn.Status);
        Assert.True(turn.Grant!.ExpiresAt < DateTimeOffset.UtcNow);
    }

    [SqlFact]
    public async Task OwnerAlivePlan_IsNeverCleaned()
    {
        Scenario s = await BuildHandoverAsync(1);
        await PassResponseDeadlineAsync(s.PlanId);
        await RunAsync(s.PlanId);
        Guid turnId = Assert.Single(await TurnsAsync(s.PlanId)).Id;
        await PassRetentionDeadlineAsync(turnId);
        // Kế hoạch về ACTIVE (như khi Verifier xác nhận Owner còn sống)
        await ApiTestHelpers.SetPlanStatusAsync(factory, s.VaultId, PlanStatus.Active);

        RetentionRunResult result = await RunAsync(s.PlanId);

        Assert.Equal(0, result.TurnsDeleted);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Assets.CountAsync(a => a.VaultId == s.VaultId && a.ContentDeletedAt != null));
        });
    }
}
