using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.ExecutorAssign;

/// <summary>
/// Admin đổi Người thực thi khi kho đang bàn giao (SQL Server thật): người mới nhận việc ngay, người cũ mất quyền ngay,
/// chữ ký người cũ đã ký trên bản hiện tại vẫn tính; kế hoạch đã đóng thì vẫn chặn.
/// </summary>
public sealed class AdminAssignExecutorDuringHandoverTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly Mail mail = new Mail();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IExecutorInvitationSender>();
            services.AddSingleton<IExecutorInvitationSender>(mail);
            services.RemoveAll<IHandoverRequestNoticeSender>();
            services.AddSingleton<IHandoverRequestNoticeSender>(mail);
        };
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

    /// <summary>Đếm lời mời Người thực thi và ghi lại thư báo đổi Người thực thi.</summary>
    private sealed class Mail : IExecutorInvitationSender, IHandoverRequestNoticeSender
    {
        private readonly object gate = new object();

        public int Invitations { get; private set; }

        public List<string> ExecutorChangedAudiences { get; } = new List<string>();

        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Invitations++;
            return Task.FromResult(true);
        }

        public Task<bool> SendAssignedByAdminAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Invitations++;
            return Task.FromResult(true);
        }

        public Task SendAsync(HandoverRequestNotice notice, CancellationToken ct)
        {
            if (notice.Stage == HandoverRequestNotice.StageExecutorChanged)
            {
                lock (gate)
                {
                    ExecutorChangedAudiences.Add(notice.Audience);
                }
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Handover
    {
        public Guid VaultId { get; set; }
        public HandoverRecordTestData.FinalizedRecord Record { get; set; } = null!;
        public HttpClient Admin { get; set; } = null!;
        public User NewExecutor { get; set; } = null!;
        public HttpClient NewExecutorClient { get; set; } = null!;
    }

    /// <summary>Kho có một người nhận được gán một tài sản; luật sư đã chốt biên bản (kho đang bàn giao).</summary>
    private async Task<Handover> BuildFinalizedHandoverAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        JsonElement recipient = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000041", ApiTestHelpers.BeneficiaryName(0), "doi-ntt-" + vaultId.ToString("N").Substring(0, 8) + "@example.com");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, recipient.GetProperty("id").GetGuid());
        HandoverRecordTestData.FinalizedRecord record = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vaultId);

        User adminUser = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient admin = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(admin, factory.Services, adminUser);
        User newExecutor = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        HttpClient newExecutorClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(newExecutorClient, factory.Services, newExecutor);

        Handover result = new Handover();
        result.VaultId = vaultId;
        result.Record = record;
        result.Admin = admin;
        result.NewExecutor = newExecutor;
        result.NewExecutorClient = newExecutorClient;
        return result;
    }

    private static Task<HttpResponseMessage> AssignAsync(Handover handover)
    {
        return handover.Admin.PostAsJsonAsync("/api/v1/admin/vaults/" + handover.VaultId + "/executor",
            new { userId = handover.NewExecutor.Id, reason = "Người thực thi cũ nghỉ việc." });
    }

    [SqlFact]
    public async Task DuringHandover_NewExecutorTakesOverAtOnce_OldOneLosesAccess_AndTheOldSignatureStays()
    {
        // Chuẩn bị: Người thực thi cũ đã ký biên bản
        Handover handover = await BuildFinalizedHandoverAsync();
        string executorRecordUrl = HandoverRecordTestData.ExecutorRecordUrl(handover.VaultId);
        string keyUrl = "/api/v1/executor/vaults/" + handover.VaultId + "/handover-key";
        await HandoverRecordTestData.ConfirmIdentitiesAsync(factory, handover.Record.RecordId);
        Guid beneficiaryId = (await HandoverRecordTestData.RecipientIdsAsync(factory, handover.Record.RecordId))[0];
        string signUrl = HandoverRecordTestData.SignUrl(executorRecordUrl, beneficiaryId);
        Assert.Equal(HttpStatusCode.OK, (await handover.Record.Executor.Client.PostAsync(signUrl, null)).StatusCode);

        // Hành động
        HttpResponseMessage assigned = await AssignAsync(handover);
        HttpResponseMessage oldRecord = await handover.Record.Executor.Client.GetAsync(executorRecordUrl);
        HttpResponseMessage oldKey = await handover.Record.Executor.Client.GetAsync(keyUrl);
        HttpResponseMessage newRecord = await handover.NewExecutorClient.GetAsync(executorRecordUrl);
        HttpResponseMessage newKey = await handover.NewExecutorClient.GetAsync(keyUrl);

        // Kiểm tra: người mới nhận việc ngay (không có lời mời), người cũ mất quyền; chữ ký cũ vẫn tính
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(assigned);
        Assert.Equal("ACCEPTED", body.GetProperty("status").GetString());
        Assert.Equal(0, mail.Invitations);
        Assert.Contains(HandoverRequestNotice.Executor, mail.ExecutorChangedAudiences);
        Assert.Contains(HandoverRequestNotice.FormerExecutor, mail.ExecutorChangedAudiences);
        Assert.Contains(HandoverRequestNotice.Verifier, mail.ExecutorChangedAudiences);
        Assert.Equal(HttpStatusCode.NotFound, oldRecord.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newRecord.StatusCode);
        JsonElement view = await ApiTestHelpers.ReadJsonAsync(newRecord);
        Assert.Equal(JsonValueKind.String, view.GetProperty("executorSignedAt").ValueKind);
        Assert.False(view.GetProperty("canSign").GetBoolean());
        Assert.Equal(handover.NewExecutor.FullName, view.GetProperty("executorName").GetString());
        Assert.Equal(HttpStatusCode.OK, newKey.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<Executor> executors = await db.Set<Executor>().Where(e => e.VaultId == handover.VaultId).ToListAsync();
            Assert.Equal(ExecutorStatus.Revoked, executors.Single(e => e.UserId == handover.Record.Executor.User.Id).Status);
            Assert.Equal(ExecutorStatus.Accepted, executors.Single(e => e.UserId == handover.NewExecutor.Id).Status);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.ExecutorAssignedByAdmin));
        });
    }

    [SqlFact]
    public async Task DuringHandover_NewExecutorSignsWhenTheOldOneHadNot()
    {
        // Chuẩn bị
        Handover handover = await BuildFinalizedHandoverAsync();
        string executorRecordUrl = HandoverRecordTestData.ExecutorRecordUrl(handover.VaultId);

        // Hành động
        HttpResponseMessage assigned = await AssignAsync(handover);
        await HandoverRecordTestData.ConfirmIdentitiesAsync(factory, handover.Record.RecordId);
        Guid beneficiaryId = (await HandoverRecordTestData.RecipientIdsAsync(factory, handover.Record.RecordId))[0];
        string signUrl = HandoverRecordTestData.SignUrl(executorRecordUrl, beneficiaryId);
        HttpResponseMessage oldSign = await handover.Record.Executor.Client.PostAsync(signUrl, null);
        HttpResponseMessage newSign = await handover.NewExecutorClient.PostAsync(signUrl, null);

        // Kiểm tra: người cũ không ký được nữa, người mới ký được
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldSign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newSign.StatusCode);
        JsonElement signed = await ApiTestHelpers.ReadJsonAsync(newSign);
        Assert.Equal(JsonValueKind.String, signed.GetProperty("executorSignedAt").ValueKind);
    }

    [SqlFact]
    public async Task ClosedPlan_IsStillBlocked()
    {
        // Chuẩn bị
        Handover handover = await BuildFinalizedHandoverAsync();
        await ApiTestHelpers.SetPlanStatusAsync(factory, handover.VaultId, PlanStatus.Closed);

        // Hành động
        HttpResponseMessage assigned = await AssignAsync(handover);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.ReadJsonAsync(assigned);
        Assert.Equal(HttpStatusCode.Conflict, assigned.StatusCode);
        Assert.Equal("PLAN_LOCKED", problem.GetProperty("reason").GetString());
    }
}
