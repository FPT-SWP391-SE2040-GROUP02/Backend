using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>EKYC-01, AC-46: điều kiện eKYC của Owner chỉ xét đúng mục đích đăng ký Owner.</summary>
public sealed class PlanSetupWarningsTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }

        this.factory = created;
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
}
