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

/// <summary>Real HTTP/SQL Executor → activation; identity/payment and mail are explicit test fixtures.</summary>
public sealed class PlanSetupFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly CapturedMail mail = new();
    public async Task InitializeAsync()
    {
        var created = ApiFactory.CreateForTempDatabase();
        if (created is null) return;
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IExecutorInvitationSender>();
            services.AddSingleton<IExecutorInvitationSender>(mail);
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
        var beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận thử nghiệm");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, beneficiary.GetProperty("id").GetGuid());
        return (owner, recipient, vaultId);
    }

    private async Task<ExecutorInvitationResponse> InviteAsync(HttpClient owner, Guid vaultId, string email = "executor@example.test")
    {
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, email);
        var response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(mail.Token, raw);
        return JsonSerializer.Deserialize<ExecutorInvitationResponse>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private Task<HttpResponseMessage> RespondAsync(HttpClient client, string token, bool acknowledge = true) => client.PostAsJsonAsync("/api/v1/executor-invitations/respond", new { token, accept = true, acknowledgeScope = acknowledge, scopeVersion = PlanSetupService.ScopeVersion, password = ApiTestHelpers.ExecutorPassword });
    private async Task<ActivatePlanRequest> ActivationAsync(HttpClient owner, Guid vaultId)
    {
        var setup = await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup");
        return new() { ConfigVersion = setup!.ConfigVersion, PolicyVersion = setup.PolicyVersion, AcknowledgePolicy = true };
    }

    [SqlFact]
    public async Task ExecutorCandidatesFindExistingAccountsOnlyAndNeverBrowseEveryone()
    {
        var (owner, vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        // Chỉ tài khoản nhân sự role EXECUTOR (Active, đã xác minh email, đã xác minh pháp lý) mới là ứng viên.
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, "ung-vien@example.test");
        await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "ung-vien-thuong@example.test");

        var found = await owner.GetFromJsonAsync<List<ExecutorCandidateResponse>>(
            $"/api/v1/vaults/{vaultId}/executor-candidates?q=ung-vien");
        Assert.Single(found!);
        Assert.Equal("ung-vien@example.test", found![0].Email);

        // Dưới 2 ký tự: rỗng, không cho duyệt toàn bộ người dùng.
        var tooShort = await owner.GetFromJsonAsync<List<ExecutorCandidateResponse>>(
            $"/api/v1/vaults/{vaultId}/executor-candidates?q=u");
        Assert.Empty(tooShort!);

        var nobody = await owner.GetFromJsonAsync<List<ExecutorCandidateResponse>>(
            $"/api/v1/vaults/{vaultId}/executor-candidates?q=khong-ton-tai");
        Assert.Empty(nobody!);
    }

    [SqlFact]
    public async Task ExecutorWithoutLegalVerificationIsListedAndCanBeInvited()
    {
        var (owner, _, vaultId) = await PrepareAsync();
        const string email = "chua-xac-minh@example.test";
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, email);
        // Không còn bước xác minh pháp lý cho Người thực thi: tài khoản chưa có LegalVerifiedAt vẫn mời được.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            User user = await db.Users.SingleAsync(u => u.Email == email);
            user.LegalVerifiedAt = null;
            await db.SaveChangesAsync();
        });

        var found = await owner.GetFromJsonAsync<List<ExecutorCandidateResponse>>(
            $"/api/v1/vaults/{vaultId}/executor-candidates?q=chua-xac-minh");
        Assert.Single(found!);

        var response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [SqlFact]
    public async Task FullHttpFlowAcceptActivateAndDuplicateActivationIsIdempotent()
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
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            var plan = await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId);
            Assert.Equal(PlanSetupService.PolicyVersion, plan.ActivationPolicyVersion);
            Assert.NotNull(plan.ActivationPolicyAcknowledgedAt);
            Assert.NotNull(plan.ActivatedAt);
            Assert.Equal(1, await db.WillVersions.CountAsync(w => w.VaultId == vaultId));
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
            Assert.Equal(0, await db.WillVersions.CountAsync());
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
    public async Task SingleExecutor_CanActivateWithoutAnyOtherExecutor()
    {
        // Đã bỏ Executor dự phòng (30/10/2026): một Người thực thi đã chấp nhận là đủ để kích hoạt.
        var (owner, primary, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
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
        Guid assignedAssetId = Guid.Empty;
        Guid assignedBeneficiaryId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            AssetAssignment assignment = await db.AssetAssignments.Where(a => a.Asset.VaultId == vaultId).SingleAsync();
            assignedAssetId = assignment.AssetId;
            assignedBeneficiaryId = assignment.BeneficiaryId;
        });
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.UnassignAssetAsync(owner, vaultId, assignedAssetId, assignedBeneficiaryId, true)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(ExecutorStatus.Suspended, (await db.Set<Executor>().SingleAsync(e => e.VaultId == vaultId)).Status);
            Assert.Equal(PlanStatus.Draft, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId)).Status);
        });
    }

    [SqlFact]
    public async Task ParallelActivationNeverCreatesTwoWillVersions()
    {
        var (owner, recipient, vaultId) = await PrepareAsync();
        await InviteAsync(owner, vaultId);
        Assert.Equal(HttpStatusCode.OK, (await RespondAsync(recipient, mail.Token)).StatusCode);
        var request = await ActivationAsync(owner, vaultId);
        var responses = await Task.WhenAll(owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request), owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        await ApiTestHelpers.WithDbAsync(factory, async db => Assert.Equal(1, await db.WillVersions.CountAsync(w => w.VaultId == vaultId)));
    }

    [SqlFact]
    public async Task ExpiredPaidServiceAndNoAssignmentAreIndependentActivationBlockers()
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
            await db.AssetAssignments.Where(a => a.Asset.VaultId == vaultId).ExecuteDeleteAsync();
        });
        response = await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup");
        // Chưa gán người nhận nào vẫn kích hoạt được; kho không còn tài sản thì không.
        Assert.DoesNotContain("ASSET_REQUIRED", response!.MissingRequirements);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
            await db.Assets.Where(a => a.VaultId == vaultId).ExecuteDeleteAsync());
        response = await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup");
        Assert.Contains("ASSET_REQUIRED", response!.MissingRequirements);
        Assert.False(response.CanActivate);
    }

    // ---- Lời mời của tôi: xem danh sách và trả lời theo id, không cần link email ----------------------------

    private static Task<HttpResponseMessage> RespondByIdAsync(HttpClient client, Guid invitationId, bool accept)
    {
        return client.PostAsJsonAsync($"/api/v1/executor-invitations/{invitationId}/respond",
            new { accept, acknowledgeScope = accept, scopeVersion = PlanSetupService.ScopeVersion, password = ApiTestHelpers.ExecutorPassword });
    }

    [SqlFact]
    public async Task MyInvitations_ListPendingThenAcceptById()
    {
        (HttpClient owner, HttpClient recipient, Guid vaultId) = await PrepareAsync();
        ExecutorInvitationResponse invitation = await InviteAsync(owner, vaultId);

        JsonElement before = await recipient.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");
        JsonElement pending = before.GetProperty("pending");
        Assert.Equal(1, pending.GetArrayLength());
        Assert.Equal(invitation.Id, pending[0].GetProperty("id").GetGuid());
        Assert.Equal("INVITED", pending[0].GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(pending[0].GetProperty("ownerName").GetString()));
        Assert.Equal(PlanSetupService.ScopeVersion, before.GetProperty("scopeVersion").GetString());
        Assert.DoesNotContain(mail.Token, before.GetRawText());

        Assert.Equal(HttpStatusCode.OK, (await RespondByIdAsync(recipient, invitation.Id, true)).StatusCode);

        JsonElement after = await recipient.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");
        Assert.Equal(0, after.GetProperty("pending").GetArrayLength());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Executor saved = await db.Set<Executor>().SingleAsync(e => e.Id == invitation.Id);
            Assert.Equal(ExecutorStatus.Accepted, saved.Status);
            Assert.Equal(PlanSetupService.ScopeVersion, saved.AcceptedScopeVersion);
        });
    }

    [SqlFact]
    public async Task MyInvitations_DeclineByIdMovesItToEnded()
    {
        (HttpClient owner, HttpClient recipient, Guid vaultId) = await PrepareAsync();
        ExecutorInvitationResponse invitation = await InviteAsync(owner, vaultId);

        Assert.Equal(HttpStatusCode.OK, (await RespondByIdAsync(recipient, invitation.Id, false)).StatusCode);

        JsonElement mine = await recipient.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");
        Assert.Equal(0, mine.GetProperty("pending").GetArrayLength());
        JsonElement ended = mine.GetProperty("ended");
        Assert.Equal(1, ended.GetArrayLength());
        Assert.Equal("DECLINED", ended[0].GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task MyInvitations_OtherEmailCannotSeeOrRespond()
    {
        (HttpClient owner, _, Guid vaultId) = await PrepareAsync();
        ExecutorInvitationResponse invitation = await InviteAsync(owner, vaultId);
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "stranger@example.test");

        JsonElement mine = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");
        Assert.Equal(0, mine.GetProperty("pending").GetArrayLength());
        await ApiTestHelpers.AssertProblemAsync(
            await RespondByIdAsync(stranger, invitation.Id, true), HttpStatusCode.NotFound, "EXECUTOR_INVITATION_INVALID");
    }

    [SqlFact]
    public async Task MyInvitations_UnverifiedEmailSeesNothing()
    {
        (HttpClient owner, _, Guid vaultId) = await PrepareAsync();
        // Tài khoản phải có trước (chỉ mời được Người thực thi hệ thống); email vẫn chưa xác minh.
        HttpClient unverified = await ApiTestHelpers.CreateClientForNewUserAsync(factory, false, "unverified@example.test");
        await InviteAsync(owner, vaultId, "unverified@example.test");

        JsonElement mine = await unverified.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");

        Assert.Equal(0, mine.GetProperty("pending").GetArrayLength());
        Assert.Equal(0, mine.GetProperty("ended").GetArrayLength());
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
