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

/// <summary>SETUP-01, SETUP-10, EKYC-01, AC-46, AC-68: dữ liệu và cảnh báo trên màn kích hoạt.</summary>
public sealed class PlanSetupWarningsTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly CapturedMail mail;

    public PlanSetupWarningsTests()
    {
        this.mail = new CapturedMail();
    }

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }

        this.factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IExecutorInvitationSender>();
            services.AddSingleton<IExecutorInvitationSender>(mail);
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

    [SqlFact]
    public async Task EKYC01_OwnerEnrollmentPassed_NewerBeneficiaryClaimFailed_NoOwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        await AddVerificationAsync(ownerId, EkycPurpose.BeneficiaryClaim, EkycStatus.Failed, createdAt.AddMinutes(1));

        // Hành động
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlanSetupResponse? setup = await response.Content.ReadFromJsonAsync<PlanSetupResponse>();
        Assert.NotNull(setup);
        Assert.DoesNotContain("OWNER_EKYC_REQUIRED", setup.MissingRequirements);
    }

    [SqlFact]
    public async Task AC46_OnlyBeneficiaryClaimPassed_OwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.BeneficiaryClaim, EkycStatus.Passed, createdAt);

        // Hành động
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlanSetupResponse? setup = await response.Content.ReadFromJsonAsync<PlanSetupResponse>();
        Assert.NotNull(setup);
        Assert.Contains("OWNER_EKYC_REQUIRED", setup.MissingRequirements);
    }

    [SqlFact]
    public async Task EKYC01_NewerOwnerAliveFailed_DoesNotHideOwnerEnrollment()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerAlive, EkycStatus.Failed, createdAt.AddMinutes(1));

        // Hành động
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlanSetupResponse? setup = await response.Content.ReadFromJsonAsync<PlanSetupResponse>();
        Assert.NotNull(setup);
        Assert.DoesNotContain("OWNER_EKYC_REQUIRED", setup.MissingRequirements);
    }

    [SqlFact]
    public async Task SETUP01_PlanSetupListsReadyAndDraftContainersButNotArchived()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, _) = await PrepareAsync();
        (Guid readyContainerId, Guid draftContainerId, _) = await PrepareContainersAsync(owner, vaultId);

        // Hành động
        JsonElement setup = await GetSetupAsync(owner, vaultId);

        // Kiểm tra
        JsonElement containers = setup.GetProperty("containers");
        Assert.Equal(2, containers.GetArrayLength());
        JsonElement ready = containers[0];
        Assert.Equal(readyContainerId, ready.GetProperty("id").GetGuid());
        Assert.Equal("Gói thử nghiệm SETUP-01", ready.GetProperty("name").GetString());
        Assert.Equal("READY", ready.GetProperty("status").GetString());
        Assert.Equal(1, ready.GetProperty("assetCount").GetInt32());
        Assert.Equal(1, ready.GetProperty("designationCount").GetInt32());
        Assert.Equal(0, ready.GetProperty("missingRequirements").GetArrayLength());

        JsonElement draft = containers[1];
        Assert.Equal(draftContainerId, draft.GetProperty("id").GetGuid());
        Assert.Equal("Gói DRAFT thử nghiệm SETUP-01", draft.GetProperty("name").GetString());
        Assert.Equal("DRAFT", draft.GetProperty("status").GetString());
        Assert.Equal(1, draft.GetProperty("assetCount").GetInt32());
        Assert.Equal(0, draft.GetProperty("designationCount").GetInt32());
        Assert.Equal(1, draft.GetProperty("missingRequirements").GetArrayLength());
        Assert.Equal("NO_DESIGNATION", draft.GetProperty("missingRequirements")[0].GetString());
    }

    [SqlFact]
    public async Task ASSET03_AC68_AssetOutsideContainerListedAndDraftAssetNotRepeated()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, _) = await PrepareAsync();
        (_, _, Guid draftAssetId) = await PrepareContainersAsync(owner, vaultId);
        Guid outsideAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Tài sản lẻ thử nghiệm ASSET-03");

        // Hành động
        JsonElement setup = await GetSetupAsync(owner, vaultId);

        // Kiểm tra
        JsonElement outsideAssets = setup.GetProperty("assetsOutsideContainer");
        Assert.Equal(1, outsideAssets.GetArrayLength());
        JsonElement outside = outsideAssets[0];
        Assert.Equal(outsideAssetId, outside.GetProperty("id").GetGuid());
        Assert.Equal("Tài sản lẻ thử nghiệm ASSET-03", outside.GetProperty("name").GetString());
        Assert.Equal("TEXT", outside.GetProperty("kind").GetString());
        Assert.NotEqual(draftAssetId, outside.GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task SETUP10_NoBackupExecutor_WarnsButActivationStillSucceeds()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        HttpClient primary = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        await InviteAsync(owner, vaultId, "primary@example.test", "PRIMARY");
        await AcceptAsync(primary);

        // Hành động
        JsonElement setup = await GetSetupAsync(owner, vaultId);
        ActivatePlanRequest request = new ActivatePlanRequest
        {
            ConfigVersion = setup.GetProperty("configVersion").GetInt32(),
            PolicyVersion = PlanSetupService.PolicyVersion,
            AcknowledgePolicy = true,
        };
        HttpResponseMessage activation = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request);

        // Kiểm tra
        AssertNoBackupWarning(setup);
        Assert.True(setup.GetProperty("canActivate").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        JsonElement activated = await ApiTestHelpers.ReadJsonAsync(activation);
        Assert.Equal("ACTIVE", activated.GetProperty("planStatus").GetString());
        AssertNoBackupWarning(activated);
        Assert.Equal(1, activated.GetProperty("containers").GetArrayLength());
        Assert.Equal(0, activated.GetProperty("assetsOutsideContainer").GetArrayLength());
    }

    [SqlFact]
    public async Task SETUP10_InvitedBackupStillWarns_AcceptedBackupClearsWarning()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        HttpClient primary = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        await InviteAsync(owner, vaultId, "primary@example.test", "PRIMARY");
        await AcceptAsync(primary);
        HttpClient backup = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        ExecutorInvitationResponse invitation = await InviteAsync(owner, vaultId, "backup@example.test", "BACKUP");

        // Hành động
        JsonElement beforeAcceptance = await GetSetupAsync(owner, vaultId);
        await AcceptAsync(backup);
        JsonElement afterAcceptance = await GetSetupAsync(owner, vaultId);

        // Kiểm tra
        Assert.Equal("INVITED", invitation.Status);
        AssertNoBackupWarning(beforeAcceptance);
        Assert.True(beforeAcceptance.GetProperty("canActivate").GetBoolean());
        Assert.Equal(0, afterAcceptance.GetProperty("warnings").GetArrayLength());
        Assert.True(afterAcceptance.GetProperty("canActivate").GetBoolean());
    }

    // SETUP-01: chuẩn bị kho trả phí và gói READY; từng test tự quyết định dữ liệu eKYC.
    private async Task<(HttpClient Owner, Guid VaultId, Guid OwnerId)> PrepareAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid ownerId = Guid.Empty;
        DateTimeOffset now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault? vault = await db.Vaults.FindAsync(vaultId);
            Assert.NotNull(vault);
            ownerId = vault.OwnerId;
            await db.Entry(vault).Reference(v => v.Entitlement).LoadAsync();
            vault.Entitlement.ServiceExpiresAt = now.AddDays(365);
            await db.SaveChangesAsync();
        });

        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Tài sản thử nghiệm SETUP-01");
        JsonElement container = await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói thử nghiệm SETUP-01");
        Guid containerId = container.GetProperty("id").GetGuid();
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "000000001017", "Người nhận thử nghiệm SETUP-01");
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();

        HttpResponseMessage addAsset = await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetId);
        Assert.Equal(HttpStatusCode.OK, addAsset.StatusCode);
        HttpResponseMessage addBeneficiary = await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            owner, vaultId, containerId, beneficiaryId);
        Assert.Equal(HttpStatusCode.OK, addBeneficiary.StatusCode);

        return (owner, vaultId, ownerId);
    }

    // SETUP-01, AC-68: bổ sung gói DRAFT có tài sản và gói đã lưu trữ qua HTTP.
    private async Task<(Guid ReadyContainerId, Guid DraftContainerId, Guid DraftAssetId)> PrepareContainersAsync(
        HttpClient owner, Guid vaultId)
    {
        HttpResponseMessage listResponse = await owner.GetAsync($"/api/v1/vaults/{vaultId}/containers");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(listResponse);
        JsonElement existingContainers = page.GetProperty("items");
        Assert.Equal(1, existingContainers.GetArrayLength());
        Guid readyContainerId = existingContainers[0].GetProperty("id").GetGuid();

        Guid draftAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Tài sản trong gói DRAFT ASSET-03");
        JsonElement draft = await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói DRAFT thử nghiệm SETUP-01");
        Guid draftContainerId = draft.GetProperty("id").GetGuid();
        HttpResponseMessage addAsset = await ApiTestHelpers.AddAssetToContainerAsync(
            owner, vaultId, draftContainerId, draftAssetId);
        Assert.Equal(HttpStatusCode.OK, addAsset.StatusCode);

        JsonElement archived = await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói lưu trữ thử nghiệm SETUP-01");
        Guid archivedContainerId = archived.GetProperty("id").GetGuid();
        HttpResponseMessage archive = await owner.DeleteAsync($"/api/v1/vaults/{vaultId}/containers/{archivedContainerId}");
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        return (readyContainerId, draftContainerId, draftAssetId);
    }

    // SETUP-10: mời và chấp nhận qua HTTP; token chỉ giữ trong RAM của test.
    private async Task<ExecutorInvitationResponse> InviteAsync(HttpClient owner, Guid vaultId, string email, string role)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ExecutorInvitationResponse? invitation = await response.Content.ReadFromJsonAsync<ExecutorInvitationResponse>();
        Assert.NotNull(invitation);
        return invitation;
    }

    private async Task AcceptAsync(HttpClient executor)
    {
        HttpResponseMessage response = await executor.PostAsJsonAsync("/api/v1/executor-invitations/respond", new
        {
            token = mail.Token,
            accept = true,
            acknowledgeScope = true,
            scopeVersion = PlanSetupService.ScopeVersion,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> GetSetupAsync(HttpClient owner, Guid vaultId)
    {
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private static void AssertNoBackupWarning(JsonElement setup)
    {
        JsonElement warnings = setup.GetProperty("warnings");
        Assert.Equal(1, warnings.GetArrayLength());
        Assert.Equal("NO_BACKUP_EXECUTOR", warnings[0].GetString());
    }

    // EKYC-01, AC-46: ghi rõ mục đích và kết quả của từng lượt bằng dữ liệu thử nghiệm.
    private async Task AddVerificationAsync(
        Guid userId, EkycPurpose purpose, EkycStatus status, DateTimeOffset createdAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            EkycVerification verification = new EkycVerification
            {
                UserId = userId,
                Purpose = purpose,
                Status = status,
                CreatedAt = createdAt,
                CompletedAt = createdAt,
                IdentityMatch = EkycIdentityMatch.NotChecked,
                CitizenIdLast4 = "",
            };
            if (status == EkycStatus.Passed)
            {
                verification.SpoofCheckPassed = true;
                verification.LivenessPassed = true;
            }

            db.EkycVerifications.Add(verification);
            await db.SaveChangesAsync();
        });
    }

    // SETUP-10: thay dịch vụ gửi thư trong test để không gửi token ra ngoài.
    private sealed class CapturedMail : IExecutorInvitationSender
    {
        public string Token { get; private set; } = "";

        public Task<bool> SendAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Token = token;
            return Task.FromResult(true);
        }
    }
}
