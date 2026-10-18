using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.IntegrationTests.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>eKYC (EkycService) bằng repository và dịch vụ eKYC giả, không cần DB.</summary>
public sealed class EkycServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeEkycVerificationRepository repository = new FakeEkycVerificationRepository();
    private readonly FakeEkycConsentRepository consentRepository = new FakeEkycConsentRepository();
    private readonly FakeHandoverRepository handoverRepository = new FakeHandoverRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaryRepository = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeEkycClient client = new FakeEkycClient();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly EkycService service;
    private readonly Guid userId = Guid.NewGuid();

    public EkycServiceTests()
    {
        currentUser.UserId = userId;
        service = new EkycService(currentUser, repository, consentRepository, handoverRepository,
            beneficiaryRepository, protector, client, unitOfWork, clock);

        // Mặc định đã đồng ý cho mục đích OWNER_ENROLLMENT để các test cũ vẫn đi đúng luồng.
        AddConsent(EkycPurpose.OwnerEnrollment, Now);
    }

    private void AddConsent(EkycPurpose purpose, DateTimeOffset at)
    {
        consentRepository.Consents.Add(new EkycConsent
        {
            UserId = userId,
            Purpose = purpose,
            NoticeVersion = EkycRules.NoticeVersion,
            ConsentedAt = at,
        });
    }

    /// <summary>Đọc mặt trước rồi qua thử thách camera: điều kiện để đường ảnh selfie tĩnh được so khớp.</summary>
    private async Task SubmitDocumentAndPassChallengeAsync()
    {
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        await service.StartLivenessAsync(CancellationToken.None);
        client.LivenessFrame = new EkycLivenessState { Passed = true, ChallengeCompleted = true, PadPassed = true };
        await service.SubmitLivenessFrameAsync(Image(), "frame.jpg", "image/jpeg", CancellationToken.None);
    }

    private static MemoryStream Image()
    {
        return new MemoryStream(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Status_NeverStarted_ReturnsNotStarted()
    {
        EkycStatusResponse response = await service.GetMyStatusAsync(CancellationToken.None);

        Assert.Equal("NOT_STARTED", response.Status);
        Assert.Null(response.VerificationId);
        Assert.Equal(EkycRules.MatchThreshold, response.MatchThreshold);
    }

    [Fact]
    public async Task Document_SavesOnlyResultAndLast4()
    {
        EkycDocumentResponse response = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);

        Assert.True(response.CanMatchFace);
        Assert.Equal("NGUYEN VAN AN", response.FullName);
        Assert.Equal(Now + EkycRules.DocumentTokenLifetime, response.ExpiresAt);
        EkycVerification saved = Assert.Single(repository.Verifications);
        Assert.Equal(EkycStatus.Pending, saved.Status);
        Assert.Equal("2345", saved.CitizenIdLast4);
        Assert.Equal("token-1", saved.DocumentToken);
        Assert.Equal(response.VerificationId, saved.Id);
    }

    [Fact]
    public async Task Document_NoFaceFound_DoesNotOpenMatching()
    {
        client.Reading = new EkycDocumentReading { IdNumber = "079200012345", DocumentToken = null };

        EkycDocumentResponse response = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);

        Assert.False(response.CanMatchFace);
        Assert.Null(response.VerificationId);
        Assert.Empty(repository.Verifications);
    }

    [Fact]
    public async Task Document_Again_CancelsPreviousToken()
    {
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        clock.Now = Now.AddMinutes(1);
        client.Reading = new EkycDocumentReading { IdNumber = "079200012345", DocumentToken = "token-2" };

        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);

        Assert.Equal(new List<string> { "token-1" }, client.DeletedTokens);
        Assert.Equal(2, repository.Verifications.Count);
        EkycVerification oldRow = repository.Verifications[0];
        Assert.Null(oldRow.DocumentToken);
    }

    [Fact]
    public async Task Document_WrongContentType_Rejected()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.gif", "image/gif", null, CancellationToken.None));

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
    }

    [Fact]
    public async Task Document_TooLarge_Rejected()
    {
        MemoryStream big = new MemoryStream(new byte[EkycRules.MaxImageBytes + 1]);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            big, "front.jpg", "image/jpeg", null, CancellationToken.None));

        Assert.Equal(ErrorCode.FileTooLarge, error.Code);
    }

    [Fact]
    public async Task Selfie_WithoutDocument_Conflict()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Fact]
    public async Task Selfie_Match_PassesWithServerThresholdAndClearsToken()
    {
        await SubmitDocumentAndPassChallengeAsync();

        EkycStatusResponse response = await service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.Equal(0.6, response.FaceMatchScore);
        Assert.Equal(EkycRules.MatchThreshold, client.LastThreshold);
        EkycVerification saved = Assert.Single(repository.Verifications);
        Assert.Null(saved.DocumentToken);
        Assert.Equal(new List<string> { "token-1" }, client.DeletedTokens);
    }

    [Fact]
    public async Task Selfie_ScoreBelowThreshold_FailsEvenIfServiceSaysMatched()
    {
        await SubmitDocumentAndPassChallengeAsync();
        client.Comparison = new EkycFaceComparison { Score = 0.2, Matched = true };

        EkycStatusResponse response = await service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("FAILED", response.Status);
    }

    [Fact]
    public async Task Selfie_Spoof_FailsWithoutMatching()
    {
        await SubmitDocumentAndPassChallengeAsync();
        client.Spoof = new EkycSpoofCheck { Passed = false, LiveScore = 0.1 };

        EkycStatusResponse response = await service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("FAILED", response.Status);
        Assert.False(response.SpoofCheckPassed);
        Assert.Null(response.FaceMatchScore);
        Assert.Null(client.LastThreshold);
    }

    [Fact]
    public async Task Selfie_SecondAttemptOnSameDocument_Conflict()
    {
        await SubmitDocumentAndPassChallengeAsync();
        await service.SubmitSelfieAsync(Image(), "selfie.jpg", "image/jpeg", CancellationToken.None);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Fact]
    public async Task Selfie_AfterTokenExpired_Conflict()
    {
        await SubmitDocumentAndPassChallengeAsync();
        clock.Now = Now + EkycRules.DocumentTokenLifetime;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Theory]
    [InlineData("079200012345", "2345")]
    [InlineData("123", "")]
    [InlineData("07920001234X", "")]
    [InlineData("", "")]
    public void GetCitizenIdLast4_OnlyReturnsFourDigits(string idNumber, string expected)
    {
        Assert.Equal(expected, EkycRules.GetCitizenIdLast4(idNumber));
    }
}
