using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Security;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.IntegrationTests.Handover;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Tạo mẫu khuôn mặt Owner khi đăng ký đạt (EKYC-22, EKYC-23, PRIV-03).</summary>
public sealed class OwnerFaceTemplateEnrollmentTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeEkycVerificationRepository repository = new FakeEkycVerificationRepository();
    private readonly FakeEkycConsentRepository consentRepository = new FakeEkycConsentRepository();
    private readonly FakeOwnerFaceTemplateRepository templates = new FakeOwnerFaceTemplateRepository();
    private readonly FaceTemplateProtector protector = FaceTemplateTestSupport.CreateProtector();
    private readonly FakeEkycClient client = new FakeEkycClient();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly EkycService service;
    private readonly Guid userId = Guid.NewGuid();

    public OwnerFaceTemplateEnrollmentTests()
    {
        currentUser.UserId = userId;
        service = new EkycService(currentUser, repository, consentRepository, new FakeHandoverRepository(),
            new FakeEkycBeneficiaryRepository(), new FakeBeneficiaryDataProtector(), templates, protector,
            new FakeOwnerIdentityRepository(), FaceTemplateTestSupport.CreateIdentityProtector(), client,
            new FakeUnitOfWork(), clock, new InMemoryAssetContentStore());
    }

    private void AddConsent(EkycPurpose purpose, DateTimeOffset consentedAt)
    {
        consentRepository.Consents.Add(new EkycConsent
        {
            UserId = userId,
            Purpose = purpose,
            NoticeVersion = EkycRules.NoticeVersion,
            ConsentedAt = consentedAt,
        });
    }

    private static MemoryStream Image()
    {
        return new MemoryStream(new byte[] { 1, 2, 3 });
    }

    private async Task<EkycVerification> StartedAsync(EkycPurpose purpose)
    {
        AddConsent(purpose, Now);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        await service.StartLivenessAsync(CancellationToken.None);
        return Assert.Single(repository.Verifications);
    }

    [Fact]
    public async Task OwnerEnrollmentPassed_StoresEncryptedTemplateFromSessionSelfie()
    {
        EkycVerification verification = await StartedAsync(EkycPurpose.OwnerEnrollment);

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        OwnerFaceTemplate template = Assert.Single(templates.Templates);
        Assert.Equal(userId, template.UserId);
        Assert.Equal(1, template.Version);
        Assert.Equal("sface", template.ModelName);
        Assert.Equal(128, template.Dimension);
        Assert.Equal(verification.Id, template.SourceVerificationId);
        Assert.Equal(Now, template.CreatedAt);
        Assert.Equal(client.Embedding.Values, protector.Unprotect(template));
        Assert.Equal(1, client.EmbeddingCalls);
    }

    [Fact]
    public async Task ExistingTemplate_IsKeptAndNotReplaced()
    {
        OwnerFaceTemplate existing = new OwnerFaceTemplate();
        existing.UserId = userId;
        existing.Version = 1;
        existing.ModelName = "sface";
        protector.Protect(existing, FaceTemplateTestSupport.Vector(9));
        templates.Templates.Add(existing);
        await StartedAsync(EkycPurpose.OwnerEnrollment);

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.Same(existing, Assert.Single(templates.Templates));
        Assert.Equal(0, client.EmbeddingCalls);
    }

    [Fact]
    public async Task FailedMatch_CreatesNoTemplate()
    {
        await StartedAsync(EkycPurpose.OwnerEnrollment);
        client.Comparison = new EkycFaceComparison { Score = 0.1, Matched = false };

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("FAILED", response.Status);
        Assert.Empty(templates.Templates);
        Assert.Equal(0, client.EmbeddingCalls);
    }

    [Fact]
    public async Task OtherPurpose_CreatesNoTemplate()
    {
        await StartedAsync(EkycPurpose.OwnerAlive);

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.Empty(templates.Templates);
        Assert.Equal(0, client.EmbeddingCalls);
    }

    [Fact]
    public async Task EmbeddingServiceDown_KeepsVerificationPending_ForRetry()
    {
        EkycVerification verification = await StartedAsync(EkycPurpose.OwnerEnrollment);
        client.EmbeddingError = new AppException(
            AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable, "Dịch vụ không khả dụng.");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
        Assert.Equal(EkycStatus.Pending, verification.Status);
        Assert.Empty(templates.Templates);
    }

    [Fact]
    public async Task StaticSelfiePath_CreatesNoTemplate()
    {
        // EKYC-22: mẫu chỉ tạo từ selfie trực tiếp của phiên thử thách, không từ ảnh tải lên.
        await StartedAsync(EkycPurpose.OwnerEnrollment);
        client.LivenessFrame = new EkycLivenessState { Passed = true };
        await service.SubmitLivenessFrameAsync(Image(), "f.jpg", "image/jpeg", CancellationToken.None);

        EkycStatusResponse response = await service.SubmitSelfieAsync(Image(), "s.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.Empty(templates.Templates);
        Assert.Equal(0, client.EmbeddingCalls);
    }
}
