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

/// <summary>SETUP-10, EKYC-01, AC-46: dữ liệu và cảnh báo trên màn kích hoạt.</summary>
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
    public async Task EKYC01_OwnerEnrollmentPassed_NewerOwnerEnrollmentFailed_NoOwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Failed, createdAt.AddMinutes(1));

        // Hành động
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlanSetupResponse? setup = await response.Content.ReadFromJsonAsync<PlanSetupResponse>();
        Assert.NotNull(setup);
        Assert.DoesNotContain("OWNER_EKYC_REQUIRED", setup.MissingRequirements);
    }

    [SqlFact]
    public async Task EKYC01_NewerOwnerEnrollmentPending_DoesNotRevokePassedEnrollment()
    {
        // Chuẩn bị: Owner đã đạt, sau đó mở lại màn eKYC và chụp giấy tờ (lượt mới đang chờ selfie).
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Pending, createdAt.AddMinutes(1));

        // Hành động
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlanSetupResponse? setup = await response.Content.ReadFromJsonAsync<PlanSetupResponse>();
        Assert.NotNull(setup);
        Assert.DoesNotContain("OWNER_EKYC_REQUIRED", setup.MissingRequirements);
    }

    [SqlFact]
    public async Task PlanSetupListsRecipientsWithTheirAssetsAndSharedCount()
    {
        // Chuẩn bị: người nhận A có 2 tài sản (một tài sản nhận chung với B), người nhận B có 1 tài sản
        (HttpClient owner, Guid vaultId, _) = await PrepareAsync();
        (Guid firstBeneficiaryId, Guid secondBeneficiaryId, Guid sharedAssetId) = await AddSharedAssignmentAsync(owner, vaultId);

        // Hành động
        JsonElement setup = await GetSetupAsync(owner, vaultId);

        // Kiểm tra: 2 tài sản đã gán, 2 người nhận
        Assert.Equal(2, setup.GetProperty("assignedAssetCount").GetInt32());
        JsonElement recipients = setup.GetProperty("recipients");
        Assert.Equal(2, recipients.GetArrayLength());
        JsonElement first = FindRecipient(recipients, firstBeneficiaryId);
        JsonElement second = FindRecipient(recipients, secondBeneficiaryId);
        Assert.Equal(2, first.GetProperty("assets").GetArrayLength());
        Assert.Equal(1, second.GetProperty("assets").GetArrayLength());
        JsonElement sharedForSecond = second.GetProperty("assets")[0];
        Assert.Equal(sharedAssetId, sharedForSecond.GetProperty("id").GetGuid());
        Assert.Equal(2, sharedForSecond.GetProperty("sharedWithCount").GetInt32());
        foreach (JsonElement asset in first.GetProperty("assets").EnumerateArray())
        {
            int expected = asset.GetProperty("id").GetGuid() == sharedAssetId ? 2 : 1;
            Assert.Equal(expected, asset.GetProperty("sharedWithCount").GetInt32());
        }
    }

    [SqlFact]
    public async Task UnassignedAssetListedAndAssignedAssetNotRepeated()
    {
        // Chuẩn bị: một tài sản đã gán (PrepareAsync) và một tài sản chưa gán cho ai
        (HttpClient owner, Guid vaultId, _) = await PrepareAsync();
        Guid looseAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Tài sản lẻ chưa gán");

        // Hành động
        JsonElement setup = await GetSetupAsync(owner, vaultId);

        // Kiểm tra: chỉ tài sản chưa gán nằm trong danh sách
        JsonElement unassigned = setup.GetProperty("unassignedAssets");
        Assert.Equal(1, unassigned.GetArrayLength());
        JsonElement loose = unassigned[0];
        Assert.Equal(looseAssetId, loose.GetProperty("id").GetGuid());
        Assert.Equal("Tài sản lẻ chưa gán", loose.GetProperty("name").GetString());
        Assert.Equal("TEXT", loose.GetProperty("kind").GetString());
    }

    [SqlFact]
    public async Task SETUP10_SingleExecutor_NoWarningsAndActivationSucceeds()
    {
        // Chuẩn bị
        (HttpClient owner, Guid vaultId, Guid ownerId) = await PrepareAsync();
        DateTimeOffset createdAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await AddVerificationAsync(ownerId, EkycPurpose.OwnerEnrollment, EkycStatus.Passed, createdAt);
        HttpClient primary = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, "primary@example.test");
        await InviteAsync(owner, vaultId, "primary@example.test");
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
        AssertNoWarnings(setup);
        Assert.True(setup.GetProperty("canActivate").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        JsonElement activated = await ApiTestHelpers.ReadJsonAsync(activation);
        Assert.Equal("ACTIVE", activated.GetProperty("planStatus").GetString());
        AssertNoWarnings(activated);
        Assert.Equal(1, activated.GetProperty("assignedAssetCount").GetInt32());
        Assert.Equal(0, activated.GetProperty("unassignedAssets").GetArrayLength());
    }

    // Chuẩn bị kho trả phí và một tài sản đã gán cho một người nhận; từng test tự quyết định dữ liệu eKYC.
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
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "000000001017", "Người nhận thử nghiệm");
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();

        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetId, beneficiaryId);

        return (owner, vaultId, ownerId);
    }

    /// <summary>
    /// Thêm người nhận thứ hai và một tài sản thứ hai gán chung cho cả hai người nhận (đồng thừa kế).
    /// Người nhận thứ nhất là người đã có từ PrepareAsync.
    /// </summary>
    private async Task<(Guid FirstBeneficiaryId, Guid SecondBeneficiaryId, Guid SharedAssetId)> AddSharedAssignmentAsync(
        HttpClient owner, Guid vaultId)
    {
        HttpResponseMessage listResponse = await owner.GetAsync($"/api/v1/vaults/{vaultId}/assignments");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        JsonElement existing = await ApiTestHelpers.ReadJsonAsync(listResponse);
        Assert.Equal(1, existing.GetArrayLength());
        Guid firstBeneficiaryId = existing[0].GetProperty("beneficiaryId").GetGuid();

        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "000000001018", ApiTestHelpers.BeneficiaryName(1));
        Guid secondBeneficiaryId = second.GetProperty("id").GetGuid();
        Guid sharedAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Tài sản nhận chung");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, sharedAssetId, firstBeneficiaryId, secondBeneficiaryId);

        return (firstBeneficiaryId, secondBeneficiaryId, sharedAssetId);
    }

    private static JsonElement FindRecipient(JsonElement recipients, Guid beneficiaryId)
    {
        foreach (JsonElement recipient in recipients.EnumerateArray())
        {
            if (recipient.GetProperty("beneficiaryId").GetGuid() == beneficiaryId)
            {
                return recipient;
            }
        }
        throw new InvalidOperationException("Không thấy người nhận trên màn kích hoạt.");
    }

    // SETUP-10: mời và chấp nhận qua HTTP; token chỉ giữ trong RAM của test.
    private async Task<ExecutorInvitationResponse> InviteAsync(HttpClient owner, Guid vaultId, string email)
    {
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, email);
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email });
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
            password = ApiTestHelpers.ExecutorPassword,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> GetSetupAsync(HttpClient owner, Guid vaultId)
    {
        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/plan-setup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    // Cảnh báo duy nhất trước đây là NO_BACKUP_EXECUTOR; đã bỏ Executor dự phòng nên mảng luôn rỗng.
    private static void AssertNoWarnings(JsonElement setup)
    {
        Assert.Equal(0, setup.GetProperty("warnings").GetArrayLength());
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
                CitizenId = "",
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

        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Token = token;
            return Task.FromResult(true);
        }
    }
}
