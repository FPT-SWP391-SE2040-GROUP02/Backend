using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// ROLE-03 / AC-67 vế người nhận: Verifier trùng email người nhận của kho bị loại khi chọn Verifier
/// và bị chặn khi quyết định, trên SQL Server thật.
/// </summary>
public sealed class VerifierBeneficiaryConflictTests : IAsyncLifetime
{
    private const string DecisionReason = "Lý do thử của Verifier.";

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
    public async Task ROLE03_BeneficiaryVerifierIsSkipped_OtherVerifierAssigned()
    {
        // Chuẩn bị: Verifier trùng email người nhận được tạo TRƯỚC (chưa từng giao nên nếu không loại thì được chọn),
        // rồi một Verifier thường.
        DeathCaseTestData.OwnerVault vault = await CreateReadyVaultAsync(true);
        User beneficiaryVerifier = await AddVerifierWithEmailAsync(ApiTestHelpers.BeneficiaryEmail);
        User normalVerifier = await DeathCaseTestData.AddVerifierAsync(factory);

        // Hành động
        Guid caseId = await SubmitCaseAsync(vault);

        // Kiểm tra
        DeathCaseAssignment? assignment = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, caseId);
        Assert.NotNull(assignment);
        Assert.Equal(normalVerifier.Id, assignment.VerifierUserId);
        Assert.NotEqual(beneficiaryVerifier.Id, assignment.VerifierUserId);
    }

    [SqlFact]
    public async Task ROLE03_OnlyBeneficiaryVerifier_CaseStaysUnassigned()
    {
        // Chuẩn bị: chỉ có Verifier trùng email người nhận
        DeathCaseTestData.OwnerVault vault = await CreateReadyVaultAsync(true);
        await AddVerifierWithEmailAsync(ApiTestHelpers.BeneficiaryEmail);

        // Hành động
        Guid caseId = await SubmitCaseAsync(vault);

        // Kiểm tra: ASSIGN-04, hồ sơ chờ giao sau
        DeathCaseAssignment? assignment = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, caseId);
        Assert.Null(assignment);
    }

    [SqlFact]
    public async Task ROLE03_AC67_AssignedVerifierBecomesBeneficiary_DecisionBlocked()
    {
        // Chuẩn bị: hồ sơ giao bình thường, sau đó email Verifier được giao trùng email người nhận
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        await SetUserEmailAsync(submitted.Verifier.User.Id, ApiTestHelpers.BeneficiaryEmail);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            DecisionReason, new[] { "FILES" });

        // Kiểm tra: bị chặn, hồ sơ vẫn UNDER_REVIEW, không thêm quyết định
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERIFIER_CONFLICT", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase deathCase = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Equal(DeathCaseStatus.UnderReview, deathCase.Status);
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task ROLE03_ApproveBlockedForBeneficiaryVerifier()
    {
        // Chuẩn bị: hồ sơ duyệt được về mọi mặt, nhưng email Verifier được giao trùng email người nhận
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        await SetUserEmailAsync(submitted.Verifier.User.Id, ApiTestHelpers.BeneficiaryEmail);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(), DecisionReason);

        // Kiểm tra: VERIFIER_CONFLICT, không có lượt bàn giao
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERIFIER_CONFLICT", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync(d => d.CaseId == submitted.CaseId));
            Assert.Equal(0, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
    }

    [SqlFact]
    public async Task ROLE03_BeneficiaryWithoutEmail_NotExcluded()
    {
        // Chuẩn bị: người nhận không khai email; Verifier có email trùng email mà người nhận các test khác dùng.
        // Giới hạn đã biết: không có email thì chưa bắt được trước F3.
        DeathCaseTestData.OwnerVault vault = await CreateReadyVaultAsync(false);
        User verifier = await AddVerifierWithEmailAsync(ApiTestHelpers.BeneficiaryEmail);

        // Hành động
        Guid caseId = await SubmitCaseAsync(vault);

        // Kiểm tra: Verifier vẫn được giao
        DeathCaseAssignment? assignment = await DeathCaseReviewTestData.LoadActiveAssignmentAsync(factory, caseId);
        Assert.NotNull(assignment);
        Assert.Equal(verifier.Id, assignment.VerifierUserId);
    }

    /// <summary>
    /// Kho trả phí có gói READY với một người nhận (có hoặc không khai email), kế hoạch ACTIVE.
    /// Chưa tạo Verifier hay Executor để test tự kiểm soát thứ tự.
    /// </summary>
    private async Task<DeathCaseTestData.OwnerVault> CreateReadyVaultAsync(bool beneficiaryHasEmail)
    {
        (HttpClient ownerClient, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(ownerClient, vaultId, "Ghi chú thừa kế");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(ownerClient, vaultId, "Gói thừa kế"))
            .GetProperty("id").GetGuid();
        HttpResponseMessage addAsset = await ApiTestHelpers.AddAssetToContainerAsync(ownerClient, vaultId, containerId, assetId);
        Assert.Equal(HttpStatusCode.OK, addAsset.StatusCode);

        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            ownerClient, vaultId, "001090000001", "Người nhận A");
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();
        if (!beneficiaryHasEmail)
        {
            // API giờ bắt buộc email, nên xóa email trong DB để giả lập hồ sơ cũ tạo trước quy tắc này.
            await RemoveBeneficiaryEmailAsync(beneficiaryId);
        }
        HttpResponseMessage designate = await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            ownerClient, vaultId, containerId, beneficiaryId);
        Assert.Equal(HttpStatusCode.OK, designate.StatusCode);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        DeathCaseTestData.OwnerVault vault = new DeathCaseTestData.OwnerVault();
        vault.Client = ownerClient;
        vault.VaultId = vaultId;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            vault.PlanId = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Id).SingleAsync();
            Guid ownerId = await db.Vaults.Where(v => v.Id == vaultId).Select(v => v.OwnerId).SingleAsync();
            vault.Owner = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerId);
        });
        return vault;
    }

    /// <summary>Giải mã dữ liệu người nhận, bỏ email rồi mã hóa và lưu lại.</summary>
    private async Task RemoveBeneficiaryEmailAsync(Guid beneficiaryId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IBeneficiaryDataProtector protector = scope.ServiceProvider.GetRequiredService<IBeneficiaryDataProtector>();
            Beneficiary beneficiary = await db.Beneficiaries.SingleAsync(b => b.Id == beneficiaryId);
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            data.Email = null;
            protector.Protect(beneficiary, data);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Executor điền đủ hồ sơ rồi gửi; trả Id hồ sơ.</summary>
    private async Task<Guid> SubmitCaseAsync(DeathCaseTestData.OwnerVault vault)
    {
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);
        return submitted.GetProperty("id").GetGuid();
    }

    /// <summary>Giống DeathCaseTestData.AddVerifierAsync nhưng đặt email cho trước.</summary>
    private async Task<User> AddVerifierWithEmailAsync(string email)
    {
        User user = new User();
        user.Email = email;
        user.FullName = "Verifier thử";
        user.Role = UserRole.Verifier;
        user.Status = UserStatus.Active;
        user.CreatedAt = DateTimeOffset.UtcNow;
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    private async Task SetUserEmailAsync(Guid userId, string email)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, email));
        });
    }
}
