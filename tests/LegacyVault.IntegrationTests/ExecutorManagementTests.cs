using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Thu hồi, đổi Executor chính khi kế hoạch đang chạy, hết hạn 7 ngày và nhắc giờ 48 (SETUP-06, SETUP-20)
/// trên nền PlanSetupService, SQL Server thật.
/// </summary>
public sealed class ExecutorManagementTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly Mail mail = new Mail();
    private readonly Reminder reminder = new Reminder();

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
            services.RemoveAll<IExecutorReminderSender>();
            services.AddSingleton<IExecutorReminderSender>(reminder);
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

    private sealed class Mail : IExecutorInvitationSender
    {
        public string Token { get; private set; } = string.Empty;

        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Token = token;
            return Task.FromResult(true);
        }
    }

    private sealed class Reminder : IExecutorReminderSender
    {
        public List<string> Sent { get; } = new List<string>();

        public bool Succeed { get; set; } = true;

        public Task<bool> SendAsync(string email, DateTimeOffset expiresAt, CancellationToken ct)
        {
            if (Succeed)
            {
                Sent.Add(email);
            }
            return Task.FromResult(Succeed);
        }
    }

    /// <summary>Kho trả phí, eKYC đạt, một gói READY; trả Owner và kho.</summary>
    private async Task<(HttpClient Owner, Guid VaultId)> PrepareOwnerAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(365)));
            db.EkycVerifications.Add(new EkycVerification
            {
                UserId = vault.OwnerId, Status = EkycStatus.Passed, CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow, SpoofCheckPassed = true,
            });
            await db.SaveChangesAsync();
        });
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói")).GetProperty("id").GetGuid();
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            owner, vaultId, containerId, beneficiary.GetProperty("id").GetGuid())).StatusCode);
        return (owner, vaultId);
    }

    private async Task<ExecutorInvitationResponse> InviteAsync(HttpClient owner, Guid vaultId, string email)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExecutorInvitationResponse>())!;
    }

    private static async Task AcceptAsync(HttpClient client, string token)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> RevokeAsync(HttpClient owner, Guid vaultId, Guid executorId, string etag)
    {
        return owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors/{executorId}/revoke", new { etag });
    }

    private async Task ActivateAsync(HttpClient owner, Guid vaultId)
    {
        PlanSetupResponse setup = (await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup"))!;
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate",
            new ActivatePlanRequest { ConfigVersion = setup.ConfigVersion, PolicyVersion = setup.PolicyVersion, AcknowledgePolicy = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SqlFact]
    public async Task Revoke_PendingInvitation_LinkDead_RepeatOk_StaleEtagConflict()
    {
        (HttpClient owner, Guid vaultId) = await PrepareOwnerAsync();
        HttpClient executor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "mot@example.test");
        ExecutorInvitationResponse invited = await InviteAsync(owner, vaultId, "mot@example.test");
        string token = mail.Token;

        HttpResponseMessage stale = await RevokeAsync(owner, vaultId, invited.Id, "\"1\"");
        await ApiTestHelpers.AssertProblemAsync(stale, HttpStatusCode.Conflict, "EDIT_CONFLICT");

        HttpResponseMessage revoked = await RevokeAsync(owner, vaultId, invited.Id, invited.Etag);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal("REVOKED", (await revoked.Content.ReadFromJsonAsync<ExecutorInvitationResponse>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await RevokeAsync(owner, vaultId, invited.Id, invited.Etag)).StatusCode);

        HttpResponseMessage accept = await executor.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion });
        Assert.False(accept.IsSuccessStatusCode);

        // Chỗ trống: mời người khác được ngay
        await InviteAsync(owner, vaultId, "hai@example.test");
    }

    [SqlFact]
    public async Task Revoke_OtherOwner_AndUnknownId_NotFound()
    {
        (HttpClient owner, Guid vaultId) = await PrepareOwnerAsync();
        (HttpClient other, Guid otherVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        ExecutorInvitationResponse invited = await InviteAsync(owner, vaultId, "mot@example.test");

        await ApiTestHelpers.AssertProblemAsync(
            await RevokeAsync(other, vaultId, invited.Id, invited.Etag), HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(
            await RevokeAsync(other, otherVaultId, invited.Id, invited.Etag), HttpStatusCode.NotFound, "EXECUTOR_NOT_FOUND");
    }

    [SqlFact]
    public async Task Setup06_ReplacePrimaryOnRunningPlan_OldKeepsRoleUntilNewAccepts()
    {
        (HttpClient owner, Guid vaultId) = await PrepareOwnerAsync();
        HttpClient oldExecutor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "cu@example.test");
        HttpClient newExecutor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "moi@example.test");
        ExecutorInvitationResponse first = await InviteAsync(owner, vaultId, "cu@example.test");
        await AcceptAsync(oldExecutor, mail.Token);
        await ActivateAsync(owner, vaultId);

        // Kế hoạch đang chạy: không thu hồi thẳng Executor chính
        PlanSetupResponse setup = (await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup"))!;
        string etag = setup.Executors.Single(e => e.Id == first.Id).Etag;
        JsonElement blocked = await ApiTestHelpers.AssertProblemAsync(
            await RevokeAsync(owner, vaultId, first.Id, etag), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        Assert.Equal("REPLACEMENT_REQUIRED", blocked.GetProperty("reason").GetString());

        // Mời người thay: người cũ vẫn là Executor chính
        ExecutorInvitationResponse replacement = await InviteAsync(owner, vaultId, "moi@example.test");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Executor old = await db.Set<Executor>().SingleAsync(e => e.Id == first.Id);
            Assert.Equal(ExecutorStatus.Accepted, old.Status);
        });

        // Người mới chấp nhận: người cũ thôi vai trò trong cùng giao dịch
        await AcceptAsync(newExecutor, mail.Token);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Executor old = await db.Set<Executor>().SingleAsync(e => e.Id == first.Id);
            Executor fresh = await db.Set<Executor>().SingleAsync(e => e.Id == replacement.Id);
            Assert.Equal(ExecutorStatus.Revoked, old.Status);
            Assert.Equal(ExecutorStatus.Accepted, fresh.Status);
            Assert.Equal(first.Id, fresh.ReplacesExecutorId);
        });
    }

    [SqlFact]
    public async Task SecondInvite_StillConflicts_WhenNoReplacementCase()
    {
        (HttpClient owner, Guid vaultId) = await PrepareOwnerAsync();
        await InviteAsync(owner, vaultId, "mot@example.test");

        HttpResponseMessage second = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors",
            new { email = "hai@example.test" });

        await ApiTestHelpers.AssertProblemAsync(second, HttpStatusCode.Conflict, "EXECUTOR_INVITATION_CONFLICT");
    }

    [SqlFact]
    public async Task Setup20_Job_RemindsOnceAt48Hours_ExpiresAfterSevenDays()
    {
        (HttpClient owner, Guid vaultId) = await PrepareOwnerAsync();
        ExecutorInvitationResponse invited = await InviteAsync(owner, vaultId, "cho@example.test");

        // Mới 47 giờ: chưa nhắc
        await MoveBackAsync(invited.Id, TimeSpan.FromHours(47));
        Assert.Empty(await FindDueAsync());

        // Qua giờ 48: nhắc đúng một lần; gửi lỗi thì không đánh dấu và vòng sau thử lại
        await MoveBackAsync(invited.Id, TimeSpan.FromHours(2));
        reminder.Succeed = false;
        Assert.Null(await ProcessAsync(invited.Id));
        reminder.Succeed = true;
        Assert.Equal("REMINDED", await ProcessAsync(invited.Id));
        Assert.Null(await ProcessAsync(invited.Id));
        Assert.Single(reminder.Sent);

        // Quá 7 ngày: job đổi sang EXPIRED, EndedAt = ExpiresAt
        await MoveBackAsync(invited.Id, TimeSpan.FromDays(6));
        Assert.Equal("EXPIRED", await ProcessAsync(invited.Id));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Executor stored = await db.Set<Executor>().SingleAsync(e => e.Id == invited.Id);
            Assert.Equal(ExecutorStatus.Expired, stored.Status);
            Assert.Equal(stored.ExpiresAt, stored.EndedAt);
        });
        Assert.Null(await ProcessAsync(invited.Id));
    }

    private Task MoveBackAsync(Guid executorId, TimeSpan by)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Executor stored = await db.Set<Executor>().SingleAsync(e => e.Id == executorId);
            stored.InvitedAt = stored.InvitedAt - by;
            stored.ExpiresAt = stored.ExpiresAt - by;
            await db.SaveChangesAsync();
        });
    }

    private async Task<List<Guid>> FindDueAsync()
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            return await scope.ServiceProvider.GetRequiredService<IExecutorInvitationJob>().FindDueIdsAsync(CancellationToken.None);
        }
    }

    private async Task<string?> ProcessAsync(Guid executorId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            return await scope.ServiceProvider.GetRequiredService<IExecutorInvitationJob>().ProcessAsync(executorId, CancellationToken.None);
        }
    }
}
