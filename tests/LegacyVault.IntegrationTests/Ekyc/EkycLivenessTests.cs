using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.IntegrationTests.Handover;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Thử thách camera (EKYC-10..14): bắt đầu, gửi khung hình, chốt bằng selfie của chính phiên.</summary>
public sealed class EkycLivenessTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeEkycVerificationRepository repository = new FakeEkycVerificationRepository();
    private readonly FakeEkycConsentRepository consentRepository = new FakeEkycConsentRepository();
    private readonly FakeEkycClient client = new FakeEkycClient();
    private readonly FakeOwnerFaceTemplateRepository templateRepository = new FakeOwnerFaceTemplateRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly EkycService service;
    private readonly Guid userId = Guid.NewGuid();

    public EkycLivenessTests()
    {
        currentUser.UserId = userId;
        service = new EkycService(currentUser, repository, consentRepository, new FakeHandoverRepository(),
            new FakeEkycBeneficiaryRepository(), new FakeBeneficiaryDataProtector(), templateRepository,
            FaceTemplateTestSupport.CreateProtector(), new FakeOwnerIdentityRepository(),
            FaceTemplateTestSupport.CreateIdentityProtector(), client, unitOfWork, clock, new InMemoryAssetContentStore(), new EkycOptions());
        consentRepository.Consents.Add(new EkycConsent
        {
            UserId = userId,
            Purpose = EkycPurpose.OwnerEnrollment,
            NoticeVersion = EkycRules.NoticeVersion,
            ConsentedAt = Now,
        });
    }

    private static MemoryStream Image()
    {
        return new MemoryStream(new byte[] { 1, 2, 3 });
    }

    private async Task ReadFrontAsync()
    {
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
    }

    private async Task<EkycVerification> StartedAsync()
    {
        await ReadFrontAsync();
        await service.StartLivenessAsync(CancellationToken.None);
        return Assert.Single(repository.Verifications);
    }

    [Fact]
    public async Task Start_StoresSessionAndHidesItFromResponse()
    {
        await ReadFrontAsync();

        EkycLivenessResponse response = await service.StartLivenessAsync(CancellationToken.None);

        Assert.Equal(2, response.Actions.Count);
        Assert.Equal("blink", response.Actions[0].Key);
        Assert.Equal(90, response.ExpiresInSeconds);
        Assert.Equal("live-1", Assert.Single(repository.Verifications).LivenessSessionId);
    }

    [Fact]
    public async Task Start_WithoutDocument_Conflict()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.StartLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
        Assert.Equal(0, client.LivenessStartCount);
    }

    [Fact]
    public async Task Start_Again_ReplacesAndDeletesPreviousSession()
    {
        EkycVerification verification = await StartedAsync();

        await service.StartLivenessAsync(CancellationToken.None);

        Assert.Equal("live-2", verification.LivenessSessionId);
        Assert.Equal(new List<string> { "live-1" }, client.DeletedLivenessSessions);
    }

    [Fact]
    public async Task Start_AfterDocumentExpired_Conflict()
    {
        await ReadFrontAsync();
        clock.Now = Now + EkycRules.DocumentTokenLifetime;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.StartLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Fact]
    public async Task Frame_UsesOwnSessionAndReturnsProgressWithoutExpiry()
    {
        await StartedAsync();
        client.LivenessFrame = new EkycLivenessState
        {
            Prompt = "Quay đầu nhẹ sang trái, giữ 1 giây, rồi nhìn thẳng",
            Calibrated = true,
            CurrentIndex = 1,
            Completed = new List<string> { "blink" },
            FrameHint = "Khung hình trùng",
        };

        EkycLivenessResponse response = await service.SubmitLivenessFrameAsync(
            Image(), "f.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal(new List<string> { "live-1" }, client.FrameSessions);
        Assert.Equal(1, response.CurrentIndex);
        Assert.Equal(new List<string> { "blink" }, response.Completed);
        Assert.True(response.Calibrated);
        Assert.False(response.Passed);
        Assert.Equal("Khung hình trùng", response.FrameHint);
        Assert.Null(response.ExpiresInSeconds);
    }

    [Fact]
    public async Task Frame_WithoutStart_LivenessRequired()
    {
        await ReadFrontAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitLivenessFrameAsync(
            Image(), "f.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessRequired, error.Code);
        Assert.Empty(client.FrameSessions);
    }

    [Fact]
    public async Task Frame_WrongContentType_Rejected()
    {
        await StartedAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitLivenessFrameAsync(
            Image(), "f.gif", "image/gif", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
    }

    [Fact]
    public async Task Complete_UsesSessionSelfie_PassesAndClosesEverything()
    {
        EkycVerification verification = await StartedAsync();

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.Equal(true, response.SpoofCheckPassed);
        Assert.Equal("live-1", client.LastSelfieSession);
        Assert.Equal(EkycRules.MatchThreshold, client.LastThreshold);
        Assert.True(verification.LivenessPassed);
        Assert.Null(verification.LivenessSessionId);
        Assert.Null(verification.DocumentToken);
        Assert.Equal(new List<string> { "token-1" }, client.DeletedTokens);
        Assert.Equal(new List<string> { "live-1" }, client.DeletedLivenessSessions);
    }

    [Fact]
    public async Task Complete_ScoreBelowThreshold_Fails()
    {
        await StartedAsync();
        client.Comparison = new EkycFaceComparison { Score = 0.1, Matched = true };

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("FAILED", response.Status);
        Assert.Equal(0.1, response.FaceMatchScore);
    }

    [Fact]
    public async Task Complete_ChallengeNotPassed_RejectedAndStillPending()
    {
        EkycVerification verification = await StartedAsync();
        client.LivenessSelfieError = new AppException(
            AppErrorType.Conflict, ErrorCode.EkycLivenessNotPassed, "Bạn chưa hoàn thành thử thách camera.");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessNotPassed, error.Code);
        Assert.Equal(EkycStatus.Pending, verification.Status);
        Assert.False(verification.LivenessPassed);
        Assert.Equal("live-1", verification.LivenessSessionId);
        Assert.Null(client.LastThreshold);
    }

    [Fact]
    public async Task Complete_WithoutStart_LivenessRequired()
    {
        await ReadFrontAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessRequired, error.Code);
    }

    [Fact]
    public async Task Complete_Twice_SecondIsConflict()
    {
        await StartedAsync();
        await service.CompleteLivenessAsync(CancellationToken.None);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Fact]
    public async Task Frame_WhenPassed_RemembersTheChallengeIsPassed()
    {
        EkycVerification verification = await StartedAsync();
        client.LivenessFrame = new EkycLivenessState { Passed = true, ChallengeCompleted = true, PadPassed = true };

        EkycLivenessResponse response = await service.SubmitLivenessFrameAsync(
            Image(), "f.jpg", "image/jpeg", CancellationToken.None);

        Assert.True(response.Passed);
        Assert.True(verification.LivenessPassed);
        Assert.Equal(EkycStatus.Pending, verification.Status);
    }

    [Fact]
    public async Task Frame_NotPassedYet_DoesNotMarkChallengePassed()
    {
        EkycVerification verification = await StartedAsync();

        await service.SubmitLivenessFrameAsync(Image(), "f.jpg", "image/jpeg", CancellationToken.None);

        Assert.False(verification.LivenessPassed);
    }

    [Fact]
    public async Task Selfie_WithoutChallenge_Rejected()
    {
        await ReadFrontAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessRequired, error.Code);
        Assert.Equal(EkycStatus.Pending, Assert.Single(repository.Verifications).Status);
        Assert.Null(client.LastThreshold);
    }

    [Fact]
    public async Task Selfie_ChallengeStartedButNotPassed_Rejected()
    {
        await StartedAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessNotPassed, error.Code);
        Assert.Null(client.LastThreshold);
    }

    [Fact]
    public async Task Selfie_AfterChallengePassed_ComparesTheUploadedPhoto()
    {
        await StartedAsync();
        client.LivenessFrame = new EkycLivenessState { Passed = true };
        await service.SubmitLivenessFrameAsync(Image(), "f.jpg", "image/jpeg", CancellationToken.None);

        EkycStatusResponse response = await service.SubmitSelfieAsync(
            Image(), "selfie.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.True(Assert.Single(repository.Verifications).LivenessPassed);
        Assert.Equal(EkycRules.MatchThreshold, client.LastThreshold);
    }

    [Fact]
    public async Task Complete_DirectPath_NeedsNoExtraSelfie()
    {
        await StartedAsync();

        EkycStatusResponse response = await service.CompleteLivenessAsync(CancellationToken.None);

        Assert.Equal("PASSED", response.Status);
        Assert.True(Assert.Single(repository.Verifications).LivenessPassed);
    }
}
