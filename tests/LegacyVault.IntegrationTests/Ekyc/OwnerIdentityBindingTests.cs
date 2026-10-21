using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Security;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.IntegrationTests.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Gắn danh tính cho Owner khi đăng ký đạt và đối chiếu các lần đăng ký sau (EKYC-24, EKYC-27, DATA-02).</summary>
public sealed class OwnerIdentityBindingTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeEkycVerificationRepository repository = new FakeEkycVerificationRepository();
    private readonly FakeEkycConsentRepository consentRepository = new FakeEkycConsentRepository();
    private readonly FakeOwnerIdentityRepository identities = new FakeOwnerIdentityRepository();
    private readonly OwnerIdentityProtector identityProtector = FaceTemplateTestSupport.CreateIdentityProtector();
    private readonly FakeEkycClient client = new FakeEkycClient();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly EkycService service;
    private readonly Guid userId = Guid.NewGuid();

    public OwnerIdentityBindingTests()
    {
        currentUser.UserId = userId;
        service = new EkycService(currentUser, repository, consentRepository, new FakeHandoverRepository(),
            new FakeEkycBeneficiaryRepository(), new FakeBeneficiaryDataProtector(), new FakeOwnerFaceTemplateRepository(),
            FaceTemplateTestSupport.CreateProtector(), identities, identityProtector, client,
            new FakeUnitOfWork(), clock);
        AddConsent(EkycPurpose.OwnerEnrollment);
    }

    private void AddConsent(EkycPurpose purpose)
    {
        consentRepository.Consents.Add(new EkycConsent
        {
            UserId = userId,
            Purpose = purpose,
            NoticeVersion = EkycRules.NoticeVersion,
            ConsentedAt = Now,
        });
    }

    private static MemoryStream Image()
    {
        return new MemoryStream(new byte[] { 1, 2, 3 });
    }

    private static EkycDocumentReading Reading(string idNumber, string fullName, string dateOfBirth, string token)
    {
        return new EkycDocumentReading
        {
            IdNumber = idNumber,
            FullName = fullName,
            DateOfBirth = dateOfBirth,
            DocumentToken = token,
        };
    }

    private Task<EkycDocumentResponse> SubmitDocumentAsync()
    {
        return service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
    }

    /// <summary>Đăng ký trọn vẹn một lượt (đọc giấy tờ → thử thách camera → chốt) với danh tính mặc định của FakeEkycClient.</summary>
    private async Task<EkycStatusResponse> EnrollAsync()
    {
        await SubmitDocumentAsync();
        await service.StartLivenessAsync(CancellationToken.None);
        return await service.CompleteLivenessAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Document_KeepsOnlyEncryptedPendingIdentity_ForOwnerEnrollment()
    {
        await SubmitDocumentAsync();

        EkycVerification verification = Assert.Single(repository.Verifications);
        Assert.NotNull(verification.EnrollmentIdentityEncrypted);
        Assert.False(verification.EnrollmentIdentityEncrypted!.AsSpan()
            .IndexOf(System.Text.Encoding.UTF8.GetBytes("079200012345")) >= 0);
        OwnerIdentityData pending = identityProtector.UnprotectPending(verification)!;
        Assert.Equal("079200012345", pending.CitizenId);
        Assert.Equal("NGUYEN VAN AN", pending.FullName);
        Assert.Equal(new DateOnly(2000, 2, 1), pending.DateOfBirth);
        Assert.Empty(identities.Identities);
    }

    [Fact]
    public async Task EnrollmentPassed_BindsEncryptedIdentity_AndClearsPending()
    {
        EkycStatusResponse response = await EnrollAsync();

        Assert.Equal("PASSED", response.Status);
        OwnerIdentity identity = Assert.Single(identities.Identities);
        Assert.Equal(userId, identity.UserId);
        Assert.Equal("2345", identity.CitizenIdLast4);
        Assert.Equal(Now, identity.CreatedAt);
        EkycVerification verification = Assert.Single(repository.Verifications);
        Assert.Equal(verification.Id, identity.SourceVerificationId);
        OwnerIdentityData data = identityProtector.Unprotect(identity);
        Assert.Equal("079200012345", data.CitizenId);
        Assert.Equal("NGUYEN VAN AN", data.FullName);
        Assert.Equal(new DateOnly(2000, 2, 1), data.DateOfBirth);
        Assert.Null(verification.EnrollmentIdentityEncrypted);
        Assert.Null(verification.EnrollmentWrappedDataKey);
        Assert.Null(verification.EnrollmentIdentityKeyId);
    }

    [Fact]
    public async Task EnrollmentFailed_BindsNothing_AndClearsPending()
    {
        await SubmitDocumentAsync();
        await service.StartLivenessAsync(CancellationToken.None);
        client.Comparison = new EkycFaceComparison { Score = 0.1, Matched = false };

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("FAILED", response.Status);
        Assert.Empty(identities.Identities);
        Assert.Null(Assert.Single(repository.Verifications).EnrollmentIdentityEncrypted);
    }

    [Fact]
    public async Task StaticSelfiePath_AlsoBindsIdentity_WhenPassed()
    {
        await SubmitDocumentAsync();
        await service.StartLivenessAsync(CancellationToken.None);
        client.LivenessFrame = new EkycLivenessState { Passed = true };
        await service.SubmitLivenessFrameAsync(Image(), "f.jpg", "image/jpeg", CancellationToken.None);

        EkycStatusResponse response = await service.SubmitSelfieAsync(Image(), "s.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.Single(identities.Identities);
    }

    [Fact]
    public async Task SecondEnrollment_AfterPassed_IsLocked_AndKeepsTheBoundIdentity()
    {
        await EnrollAsync();
        OwnerIdentity bound = Assert.Single(identities.Identities);
        clock.Now = Now.AddMinutes(1);
        client.Reading = Reading("079200012345", "NGUYEN VAN AN", "01/02/2000", "token-2");

        AppException error = await Assert.ThrowsAsync<AppException>(() => SubmitDocumentAsync());

        Assert.Equal(ErrorCode.EkycAlreadyVerified, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Same(bound, Assert.Single(identities.Identities));
        Assert.Equal(EkycStatus.Passed, Assert.Single(repository.Verifications).Status);
    }

    [Fact]
    public async Task Consent_AfterPassed_IsLocked()
    {
        await EnrollAsync();
        EkycConsentRequest request = new EkycConsentRequest();
        request.Purpose = "OWNER_ENROLLMENT";
        request.NoticeVersion = EkycRules.NoticeVersion;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.RecordConsentAsync(request, CancellationToken.None));

        Assert.Equal(ErrorCode.EkycAlreadyVerified, error.Code);
    }

    [Fact]
    public async Task SecondEnrollment_AfterFailed_IsAllowed_AndKeepsTheBoundIdentity()
    {
        await EnrollAsync();
        OwnerIdentity bound = Assert.Single(identities.Identities);
        repository.Verifications[0].Status = EkycStatus.Failed;
        clock.Now = Now.AddMinutes(1);
        client.Reading = Reading("079200012345", "nguyen  van an", "2000-02-01", "token-2");

        EkycStatusResponse response = await EnrollAsync();

        Assert.Equal("PASSED", response.Status);
        Assert.Same(bound, Assert.Single(identities.Identities));
        Assert.Equal(2, repository.Verifications.Count);
    }

    [Fact]
    public async Task SecondEnrollment_OtherCitizenId_IsRejected_AndFaceIsDiscarded()
    {
        await EnrollAsync();
        repository.Verifications[0].Status = EkycStatus.Failed;
        clock.Now = Now.AddMinutes(1);
        client.Reading = Reading("079200099999", "NGUYEN VAN AN", "01/02/2000", "token-2");

        AppException error = await Assert.ThrowsAsync<AppException>(() => SubmitDocumentAsync());

        Assert.Equal(ErrorCode.EkycIdentityMismatch, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Contains("token-2", client.DeletedTokens);
        Assert.Single(repository.Verifications);
    }

    [Fact]
    public async Task SecondEnrollment_SameIdButOtherName_AsksToRetake()
    {
        await EnrollAsync();
        repository.Verifications[0].Status = EkycStatus.Failed;
        clock.Now = Now.AddMinutes(1);
        client.Reading = Reading("079200012345", "TRAN THI BICH", "01/02/2000", "token-2");

        AppException error = await Assert.ThrowsAsync<AppException>(() => SubmitDocumentAsync());

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
        Assert.Contains("token-2", client.DeletedTokens);
        Assert.Single(repository.Verifications);
    }

    [Fact]
    public async Task Enrollment_UnreadableIdentity_AsksToRetake_AndDiscardsFace()
    {
        client.Reading = Reading(string.Empty, "NGUYEN VAN AN", "01/02/2000", "token-9");

        AppException error = await Assert.ThrowsAsync<AppException>(() => SubmitDocumentAsync());

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
        Assert.Contains("token-9", client.DeletedTokens);
        Assert.Empty(repository.Verifications);
    }

    [Fact]
    public async Task OtherPurpose_DoesNotNeedOrKeepIdentity()
    {
        AddConsent(EkycPurpose.OwnerAlive);
        consentRepository.Consents[1].ConsentedAt = Now.AddSeconds(1);
        client.Reading = Reading(string.Empty, string.Empty, string.Empty, "token-5");

        await SubmitDocumentAsync();

        EkycVerification verification = Assert.Single(repository.Verifications);
        Assert.Null(verification.EnrollmentIdentityEncrypted);
    }

    [Fact]
    public async Task NewDocument_ReplacesPendingIdentityOfPreviousAttempt()
    {
        await SubmitDocumentAsync();
        EkycVerification first = Assert.Single(repository.Verifications);
        clock.Now = Now.AddMinutes(1);
        client.Reading = Reading("079200012345", "NGUYEN VAN AN", "01/02/2000", "token-2");

        await SubmitDocumentAsync();

        Assert.Null(first.EnrollmentIdentityEncrypted);
        Assert.NotNull(repository.Verifications[1].EnrollmentIdentityEncrypted);
    }
}
