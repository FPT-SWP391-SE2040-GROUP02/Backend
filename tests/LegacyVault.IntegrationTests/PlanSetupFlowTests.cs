using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using LegacyVault.Infrastructure.Data;
using LegacyVault.Application.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>Real HTTP/SQL Executor → activation → check-in; identity/payment and mail are explicit test fixtures.</summary>
public sealed class PlanSetupFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly CapturedMail mail = new();
    private bool failAfterOpening;
    public async Task InitializeAsync()
    {
        var created = ApiFactory.CreateForTempDatabase();
        if (created is null) return;
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IExecutorInvitationSender>();
            services.AddSingleton<IExecutorInvitationSender>(mail);
            services.AddScoped<ICheckInScheduler>(provider => new FaultInjectingScheduler(
                new CheckInScheduler(provider.GetRequiredService<ICheckInRepository>(), provider.GetRequiredService<TimeProvider>()),
                provider.GetRequiredService<AppDbContext>(), () => failAfterOpening));
        };
        await factory.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (factory is null) return;
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private async Task<(HttpClient Owner, HttpClient Recipient, Guid VaultId)> PrepareAsync(bool passedEkyc = true)
    {
        var (owner, vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        // M4: người nhận phải dùng đúng email được mời (InviteAsync mặc định mời executor@example.test).
        var recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "executor@example.test");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            var vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId).ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(365)));
            if (passedEkyc) db.EkycVerifications.Add(new EkycVerification { UserId = vault.OwnerId, Status = EkycStatus.Passed, CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow, SpoofCheckPassed = true });
            await db.SaveChangesAsync();
        });
        var asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "E2E fixture asset");
        var container = await ApiTestHelpers.ReadJsonAsync(await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/containers", new { name = "E2E fixture container" }));
        var containerId = container.GetProperty("id").GetGuid();
        var beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận thử nghiệm");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/containers/{containerId}/assets", new { assetId = asset })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/containers/{containerId}/beneficiaries", new { beneficiaryId = beneficiary.GetProperty("id").GetGuid() })).StatusCode);
        return (owner, recipient, vaultId);
    }

    private async Task<ExecutorInvitationResponse> InviteAsync(HttpClient owner, Guid vaultId, Guid? beneficiaryId = null, string email = "executor@example.test")
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email, beneficiaryId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(mail.Token, raw);
        return JsonSerializer.Deserialize<ExecutorInvitationResponse>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private Task<HttpResponseMessage> RespondAsync(HttpClient client, string token, bool acknowledge = true) => client.PostAsJsonAsync("/api/v1/executor-invitations/respond", new { token, accept = true, acknowledgeScope = acknowledge, scopeVersion = PlanSetupService.ScopeVersion });
    private async Task<ActivatePlanRequest> ActivationAsync(HttpClient owner, Guid vaultId)
    {
        var setup = await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup");
        return new() { ConfigVersion = setup!.ConfigVersion, PolicyVersion = setup.PolicyVersion, AcknowledgePolicy = true };
    }

    [SqlFact]
    public async Task FullHttpFlowAcceptActivateCheckInAndDuplicatesPreserveSingleOpenPeriod()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        var request = await ActivationAsync(owner, vaultId);
        var activation = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request);
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        Assert.Equal("ACTIVE", (await activation.Content.ReadFromJsonAsync<PlanSetupResponse>())!.PlanStatus);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request)).StatusCode);
        var first = await owner.GetFromJsonAsync<JsonElement>("/api/v1/check-ins/me");
        Assert.Equal(1, first.GetProperty("history").GetProperty("totalCount").GetInt32());
        var checkedIn = await owner.PostAsync("/api/v1/check-ins", null);
        Assert.Equal(HttpStatusCode.OK, checkedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync("/api/v1/check-ins", null)).StatusCode);
        var reloaded = await owner.GetFromJsonAsync<JsonElement>("/api/v1/check-ins/me");
        Assert.Equal(2, reloaded.GetProperty("history").GetProperty("totalCount").GetInt32());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            var plan = await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId);
            Assert.Equal(PlanSetupService.PolicyVersion, plan.ActivationPolicyVersion);
            Assert.NotNull(plan.ActivationPolicyAcknowledgedAt);
            Assert.NotNull(plan.ActivatedAt);
            Assert.Equal(1, await db.CheckInPeriods.CountAsync(p => p.PlanId == plan.Id && p.ClosedAt == null));
            Assert.Equal(ExecutorStatus.Accepted, (await db.Set<Executor>().SingleAsync(e => e.VaultId == vaultId)).Status);
        });
    }

    [SqlFact]
    public async Task MissingEkycOrScopeAcknowledgementNeverActivatesOrBinds()
    {
        var (owner, recipient, vaultId) = await PrepareAsync(false);
        await InviteAsync(owner, vaultId);
        await ApiTestHelpers.AssertProblemAsync(await RespondAsync(recipient, mail.Token, false), HttpStatusCode.BadRequest, "POLICY_ACKNOWLEDGEMENT_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        var request = await ActivationAsync(owner, vaultId);
        var denied = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request);
        var body = await ApiTestHelpers.AssertProblemAsync(denied, HttpStatusCode.Conflict, "PLAN_ACTIVATION_REQUIREMENTS_NOT_MET");
        Assert.Contains("OWNER_EKYC_REQUIRED", body.GetProperty("missingRequirements").EnumerateArray().Select(e => e.GetString()));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.CheckInPeriods.CountAsync());
            Assert.Equal(PlanStatus.Draft, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId)).Status);
        });
    }

    [SqlFact]
    public async Task WrongOwnerSelfAcceptanceAnotherClaimantAndResendOldTokenAreDenied()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        var invitation = await InviteAsync(owner, vaultId);
        var oldToken = mail.Token;
        await ApiTestHelpers.AssertProblemAsync(await recipient.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup"), HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(await RespondAsync(owner, oldToken), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors/{invitation.Id}/resend", new { invitation.Etag })).StatusCode);
        await ApiTestHelpers.AssertProblemAsync(await RespondAsync(recipient, oldToken), HttpStatusCode.NotFound, "EXECUTOR_INVITATION_INVALID");
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        var other = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        await ApiTestHelpers.AssertProblemAsync(await RespondAsync(other, mail.Token), HttpStatusCode.Conflict, "EXECUTOR_INVITATION_CONFLICT");
    }

    [SqlFact]
    public async Task AccountWithDifferentEmailCannotAcceptForwardedInvitation()
    {
        // M4, SETUP-20: link bị chuyển tiếp cho tài khoản email khác thì không nhận được vai trò Executor
        var (owner, recipient, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
        var stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);

        var denied = await RespondAsync(stranger, mail.Token);

        await ApiTestHelpers.AssertProblemAsync(denied, HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            var executor = await db.Set<Executor>().SingleAsync(e => e.VaultId == vaultId);
            Assert.Equal(ExecutorStatus.Invited, executor.Status);
            Assert.Null(executor.UserId);
        });
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
    }

    [SqlFact]
    public async Task ActivationRejectsStaleConfigPolicyAndMissingExecutor()
    {
        var (owner, _, vaultId) = await PrepareAsync();
        var request = await ActivationAsync(owner, vaultId);
        request.AcknowledgePolicy = false;
        await ApiTestHelpers.AssertProblemAsync(await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request), HttpStatusCode.BadRequest, "POLICY_ACKNOWLEDGEMENT_REQUIRED");
        request.AcknowledgePolicy = true;
        request.ConfigVersion--;
        await ApiTestHelpers.AssertProblemAsync(await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request), HttpStatusCode.Conflict, "EDIT_CONFLICT");
        request.ConfigVersion++;
        await ApiTestHelpers.AssertProblemAsync(await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request), HttpStatusCode.Conflict, "PLAN_ACTIVATION_REQUIREMENTS_NOT_MET");
    }

    [SqlFact]
    public async Task ExpiredInvitationAndUnverifiedRecipientCannotAccept()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        var invitation = await InviteAsync(owner, vaultId);
        var unverified = await ApiTestHelpers.CreateClientForNewUserAsync(factory, false);
        await ApiTestHelpers.AssertProblemAsync(await RespondAsync(unverified, mail.Token), HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");
        await ApiTestHelpers.WithDbAsync(factory, async db => await db.Set<Executor>().Where(e => e.Id == invitation.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await ApiTestHelpers.AssertProblemAsync(await RespondAsync(recipient, mail.Token), HttpStatusCode.NotFound, "EXECUTOR_INVITATION_INVALID");
    }

    [SqlFact]
    public async Task ExecutorWhoIsBeneficiary_CanActivateWithoutAnyOtherExecutor()
    {
        // Đã bỏ Executor dự phòng (30/10/2026): Executor kiêm người nhận không còn đòi người dự phòng độc lập.
        var (owner, primary, vaultId) = await PrepareAsync();
        Guid beneficiaryId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db => beneficiaryId = await db.Beneficiaries.Where(b => b.VaultId == vaultId).Select(b => b.Id).SingleAsync());
        await InviteAsync(owner, vaultId, beneficiaryId: beneficiaryId);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(primary, mail.Token)).StatusCode);
        var request = await ActivationAsync(owner, vaultId);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request)).StatusCode);
    }

    [SqlFact]
    public async Task UndeliveredMailIsVisibleAndDeactivationSuspendsAcceptedExecutor()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        mail.Delivered = false;
        var invitation = await InviteAsync(owner, vaultId);
        Assert.Null(invitation.InvitationSentAt);
        mail.Delivered = true;
        var resent = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors/{invitation.Id}/resend", new { invitation.Etag });
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", await ActivationAsync(owner, vaultId))).StatusCode);
        Guid containerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db => containerId = await db.Containers.Where(c => c.VaultId == vaultId).Select(c => c.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/v1/vaults/{vaultId}/containers/{containerId}?confirmPlanDeactivation=true")).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(ExecutorStatus.Suspended, (await db.Set<Executor>().SingleAsync(e => e.VaultId == vaultId)).Status);
            Assert.Equal(PlanStatus.Draft, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId)).Status);
            Assert.Equal(0, await db.CheckInPeriods.CountAsync(p => p.ClosedAt == null));
        });
    }

    [SqlFact]
    public async Task ParallelActivationNeverCreatesTwoFirstPeriods()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        var request = await ActivationAsync(owner, vaultId);
        var responses = await Task.WhenAll(owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request), owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        await ApiTestHelpers.WithDbAsync(factory, async db => Assert.Equal(1, await db.CheckInPeriods.CountAsync()));
    }

    [SqlFact]
    public async Task SchedulerFailureRollsBackEvenAfterFlushingPlanAndPeriodInsideTransaction()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        failAfterOpening = true;
        var failed = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", await ActivationAsync(owner, vaultId));
        await ApiTestHelpers.AssertProblemAsync(failed, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            var plan = await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId);
            Assert.Equal(PlanStatus.Draft, plan.Status);
            Assert.Null(plan.ActivatedAt);
            Assert.Null(plan.ActivationPolicyAcknowledgedAt);
            Assert.Equal(0, await db.CheckInPeriods.CountAsync());
            Assert.Equal(0, await db.CheckInSchedules.CountAsync());
        });
    }

    [SqlFact]
    public async Task ExpiredPaidServiceAndNoReadyContainerAreIndependentActivationBlockers()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db => await db.VaultEntitlements.Where(e => e.VaultId == vaultId).ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        var response = await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup");
        Assert.Contains("PAID_SERVICE_REQUIRED", response!.MissingRequirements);
        Assert.False(response.CanActivate);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId).ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(30)));
            await db.Containers.Where(c => c.VaultId == vaultId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContainerStatus.Draft));
        });
        response = await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup");
        Assert.Contains("READY_CONTAINER_REQUIRED", response!.MissingRequirements);
        Assert.False(response.CanActivate);
    }

    private sealed class FaultInjectingScheduler(ICheckInScheduler inner, AppDbContext db, Func<bool> shouldFail) : ICheckInScheduler
    {
        public async Task StartFirstPeriodAsync(HandoverPlan plan, CancellationToken ct)
        {
            await inner.StartFirstPeriodAsync(plan, ct);
            if (shouldFail())
            {
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException("Synthetic scheduler failure after flush");
            }
        }
        public Task StopAsync(HandoverPlan plan, CancellationToken ct) => inner.StopAsync(plan, ct);
    }

    private sealed class CapturedMail : IExecutorInvitationSender
    {
        // Only test RAM; never expose token through response, assertion output or a real SMTP server.
        public string Token { get; private set; } = "";
        public bool Delivered { get; set; } = true;
        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        { Token = token; return Task.FromResult(Delivered); }
    }
}
