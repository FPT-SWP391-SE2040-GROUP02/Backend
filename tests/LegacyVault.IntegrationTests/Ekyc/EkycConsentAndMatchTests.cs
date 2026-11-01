using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.IntegrationTests.Handover;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>
/// eKYC: đồng ý trước khi chụp (EKYC-03), mặt sau (EKYC-04) và đối chiếu với người được chỉ định (MATCH-01..07),
/// bằng repository và dịch vụ eKYC giả, không cần DB.
/// </summary>
public sealed class EkycConsentAndMatchTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeEkycVerificationRepository repository = new FakeEkycVerificationRepository();
    private readonly FakeEkycConsentRepository consentRepository = new FakeEkycConsentRepository();
    private readonly FakeHandoverRepository handoverRepository = new FakeHandoverRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaryRepository = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeEkycClient client = new FakeEkycClient();
    private readonly IOwnerIdentityProtector identityProtector = FaceTemplateTestSupport.CreateIdentityProtector();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Now);
    private readonly EkycService service;
    private readonly Guid userId = Guid.NewGuid();

    public EkycConsentAndMatchTests()
    {
        currentUser.UserId = userId;
        service = new EkycService(currentUser, repository, consentRepository, handoverRepository,
            beneficiaryRepository, protector, new FakeOwnerFaceTemplateRepository(), FaceTemplateTestSupport.CreateProtector(),
            new FakeOwnerIdentityRepository(), identityProtector, client, new FakeUnitOfWork(), clock, new InMemoryAssetContentStore(), new EkycOptions());
    }

    private static MemoryStream Image()
    {
        return new MemoryStream(new byte[] { 1, 2, 3 });
    }

    private static BeneficiaryPersonalData Person()
    {
        return new BeneficiaryPersonalData
        {
            CitizenId = "079200012345",
            FullName = "Nguyễn Văn An",
            DateOfBirth = new DateOnly(2000, 2, 1),
        };
    }

    private static EkycDocumentReading Reading(string idNumber, string fullName, string dateOfBirth)
    {
        return new EkycDocumentReading
        {
            IdNumber = idNumber,
            FullName = fullName,
            DateOfBirth = dateOfBirth,
            DocumentToken = "token-1",
        };
    }

    private void Consent(EkycPurpose purpose, DateTimeOffset at, string noticeVersion)
    {
        consentRepository.Consents.Add(new EkycConsent
        {
            UserId = userId,
            Purpose = purpose,
            NoticeVersion = noticeVersion,
            ConsentedAt = at,
        });
    }

    /// <summary>Dựng một lượt bàn giao đang ở bước xác minh danh tính, thuộc về người đang gọi.</summary>
    private HandoverTurn AddTurn(HandoverTurnStatus status, Guid claimantId)
    {
        Beneficiary beneficiary = new Beneficiary { VaultId = Guid.NewGuid() };
        beneficiaryRepository.Beneficiaries.Add(beneficiary);
        protector.Data[beneficiary.Id] = Person();

        HandoverTurn turn = new HandoverTurn
        {
            VaultId = beneficiary.VaultId,
            BeneficiaryId = beneficiary.Id,
            ClaimantUserId = claimantId,
            Status = status,
        };
        handoverRepository.Turns.Add(turn);
        return turn;
    }

    [Fact]
    public async Task Consent_Recorded_WithPurposeVersionAndExpiry()
    {
        EkycConsentResponse response = await service.RecordConsentAsync(
            new EkycConsentRequest { Purpose = "BENEFICIARY_CLAIM", NoticeVersion = EkycRules.NoticeVersion },
            CancellationToken.None);

        Assert.Equal("BENEFICIARY_CLAIM", response.Purpose);
        Assert.Equal(Now + EkycRules.ConsentLifetime, response.ExpiresAt);
        EkycConsent saved = Assert.Single(consentRepository.Consents);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal(EkycPurpose.BeneficiaryClaim, saved.Purpose);
        Assert.Equal(Now, saved.ConsentedAt);
    }

    [Theory]
    [InlineData("OwnerEnrollment", "2026-10-v1")]
    [InlineData("1", "2026-10-v1")]
    [InlineData("OWNER_ENROLLMENT", "cu")]
    public async Task Consent_UnknownPurposeOrOldNotice_Rejected(string purpose, string noticeVersion)
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.RecordConsentAsync(
            new EkycConsentRequest { Purpose = purpose, NoticeVersion = noticeVersion }, CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
        Assert.Empty(consentRepository.Consents);
    }

    [Fact]
    public async Task Document_WithoutConsent_Conflict()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", null, CancellationToken.None));

        Assert.Equal(ErrorCode.EkycConsentRequired, error.Code);
        Assert.Empty(repository.Verifications);
    }

    [Fact]
    public async Task Document_ConsentExpired_Conflict()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now.AddMinutes(-31), EkycRules.NoticeVersion);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", null, CancellationToken.None));

        Assert.Equal(ErrorCode.EkycConsentRequired, error.Code);
    }

    [Fact]
    public async Task Document_ConsentForOldNotice_Conflict()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, "cu");

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", null, CancellationToken.None));

        Assert.Equal(ErrorCode.EkycConsentRequired, error.Code);
    }

    [Fact]
    public async Task Document_OwnerPurpose_StoresPurposeAndNotChecked()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);

        EkycDocumentResponse response = await service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);

        Assert.Equal("NOT_CHECKED", response.IdentityMatch);
        EkycVerification saved = Assert.Single(repository.Verifications);
        Assert.Equal(EkycPurpose.OwnerEnrollment, saved.Purpose);
        Assert.Equal(EkycIdentityMatch.NotChecked, saved.IdentityMatch);
        Assert.Null(saved.HandoverTurnId);
    }

    [Fact]
    public async Task Document_OwnerPurposeWithTurnId_Rejected()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
    }

    [Fact]
    public async Task Claim_WithoutTurnId_Rejected()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", null, CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
    }

    [Fact]
    public async Task Claim_SamePerson_Matched()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, userId);
        client.Reading = Reading("079200012345", "NGUYỄN  VĂN AN", "2000-02-01");

        EkycDocumentResponse response = await service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", turn.Id, CancellationToken.None);

        Assert.Equal("MATCHED", response.IdentityMatch);
        EkycVerification saved = Assert.Single(repository.Verifications);
        Assert.Equal(EkycPurpose.BeneficiaryClaim, saved.Purpose);
        Assert.Equal(turn.Id, saved.HandoverTurnId);
        Assert.Equal(EkycIdentityMatch.Matched, saved.IdentityMatch);
    }

    [Fact]
    public async Task Claim_DifferentCitizenId_WrongPersonAndTurnUntouched()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, userId);
        client.Reading = Reading("079200099999", "NGUYỄN VĂN AN", "2000-02-01");

        EkycDocumentResponse response = await service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", turn.Id, CancellationToken.None);

        Assert.Equal("WRONG_PERSON", response.IdentityMatch);
        // MATCH-05 chỉ ghi kết quả; đóng lượt là việc của luồng bàn giao, không phải của eKYC.
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Claim_OtherUsersTurn_NotFound()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, Guid.NewGuid());

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
        Assert.Empty(repository.Verifications);
    }

    [Fact]
    public async Task Claim_TurnNotInIdentityStep_Conflict()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept, userId);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitDocumentAsync(
            Image(), "front.jpg", "image/jpeg", turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
    }

    [Theory]
    [InlineData("079200012345", "Nguyễn Văn An", "2000-02-01", EkycIdentityMatch.Matched)]
    [InlineData("079200012345", "Nguyễn Văn An", "01/02/2000", EkycIdentityMatch.Matched)]
    [InlineData("079200012345", "Nguyễn Văn An", "2000-02-02", EkycIdentityMatch.NeedsReview)]
    [InlineData("079200012345", "  nguyễn   văn an ", "2000-02-01", EkycIdentityMatch.Matched)]
    [InlineData("079200012345", "Nguyen Van An", "01/02/2000", EkycIdentityMatch.NeedsReview)]
    [InlineData("079200012345", "Nguyễn Văn An", "02/02/2000", EkycIdentityMatch.NeedsReview)]
    [InlineData("079200012345", "Nguyễn Văn An", "khong ro", EkycIdentityMatch.NeedsReview)]
    [InlineData("0792000123", "Nguyễn Văn An", "01/02/2000", EkycIdentityMatch.NeedsReview)]
    [InlineData("", "Nguyễn Văn An", "01/02/2000", EkycIdentityMatch.NeedsReview)]
    [InlineData("079200099999", "Nguyễn Văn An", "01/02/2000", EkycIdentityMatch.WrongPerson)]
    public void CompareIdentity_FollowsMatchRules(
        string idNumber, string fullName, string dateOfBirth, EkycIdentityMatch expected)
    {
        EkycIdentityMatch actual = EkycRules.CompareIdentity(Reading(idNumber, fullName, dateOfBirth), Person());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompareIdentity_DifferentIdWithOcrWarning_NeedsReviewNotWrongPerson()
    {
        EkycDocumentReading reading = Reading("079200099999", "Nguyễn Văn An", "01/02/2000");
        reading.Warnings.Add("Có dòng chữ với điểm OCR thấp; hãy đối chiếu ảnh gốc.");

        Assert.Equal(EkycIdentityMatch.NeedsReview, EkycRules.CompareIdentity(reading, Person()));
    }

    [Fact]
    public void CompareIdentity_NameIsNfcNormalized()
    {
        // "ế" dựng sẵn (U+1EBF) và dạng tổ hợp (e + U+0302 + U+0301) phải coi là một.
        BeneficiaryPersonalData person = Person();
        person.FullName = "Trần Hiếu";
        EkycDocumentReading reading = Reading("079200012345", "Trần Hiếu", "01/02/2000");

        Assert.Equal(EkycIdentityMatch.Matched, EkycRules.CompareIdentity(reading, person));
    }

    [Fact]
    public async Task Back_SameCard_SetsBackSideChecked()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);

        EkycStatusResponse response = await service.SubmitBackAsync(
            Image(), "back.jpg", "image/jpeg", CancellationToken.None);

        Assert.True(response.BackSideChecked);
        Assert.True(Assert.Single(repository.Verifications).BackSideChecked);
    }

    [Fact]
    public async Task Back_ReturnsResidenceAddressFromBack_BeforeAnyConfirmation()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        client.BackReading = new EkycBackReading { IdNumber = "079200012345", ResidenceAddress = "12 Đường Thử, Quận 1, TP. Hồ Chí Minh" };

        // Chưa gọi ConfirmDocumentAsync: mặt sau vẫn được nhận để người dùng có địa chỉ đối chiếu rồi mới xác nhận (BR-88).
        EkycBackResponse response = await service.SubmitBackAsync(
            Image(), "back.jpg", "image/jpeg", CancellationToken.None);

        Assert.True(response.BackSideChecked);
        Assert.Equal("12 Đường Thử, Quận 1, TP. Hồ Chí Minh", response.ResidenceAddress);
    }

    [Fact]
    public async Task Back_NoAddressOnBack_ReturnsEmptyAddress()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        client.BackReading = new EkycBackReading { IdNumber = "079200012345", ResidenceAddress = string.Empty };

        EkycBackResponse response = await service.SubmitBackAsync(
            Image(), "back.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal(string.Empty, response.ResidenceAddress);
    }

    [Fact]
    public async Task Back_AddressIsNeverStoredOrReturnedByStatus()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        client.BackReading = new EkycBackReading { IdNumber = "079200012345", ResidenceAddress = "Địa chỉ không được lưu" };

        await service.SubmitBackAsync(Image(), "back.jpg", "image/jpeg", CancellationToken.None);

        // Lượt eKYC và phản hồi trạng thái (GET /ekyc/me) không có chỗ chứa địa chỉ: chỉ phản hồi mặt sau mới có.
        EkycVerification stored = Assert.Single(repository.Verifications);
        Assert.Null(typeof(EkycVerification).GetProperty("ResidenceAddress"));
        Assert.Null(typeof(EkycStatusResponse).GetProperty("ResidenceAddress"));
        Assert.True(stored.BackSideChecked);
    }

    private static EkycDocumentConfirmationRequest Confirmation(Guid id, string purpose = "OWNER_ENROLLMENT", Guid? turnId = null)
    {
        return new EkycDocumentConfirmationRequest
        {
            VerificationId = id, Purpose = purpose, HandoverTurnId = turnId,
            IdNumber = "079200012345", FullName = "Nguyễn Văn An",
            DateOfBirth = new DateOnly(2000, 2, 1), ResidenceAddress = "Địa chỉ thử nghiệm",
        };
    }

    [Fact]
    public async Task Confirm_OwnerAcceptsCorrectedFieldsWithoutChangingVerification()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        EkycDocumentResponse front = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, default);
        EkycStatusResponse result = await service.ConfirmDocumentAsync(Confirmation(front.VerificationId!.Value), default);
        Assert.Equal(front.VerificationId, result.VerificationId);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal("NOT_CHECKED", result.IdentityMatch);
        Assert.False(result.BackSideChecked);
    }

    [Fact]
    public async Task Confirm_OwnerEnrollment_StoresConfirmedAddressEncryptedOnPendingIdentity()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        EkycDocumentResponse front = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, default);
        EkycVerification stored = Assert.Single(repository.Verifications);
        Assert.Equal(string.Empty, identityProtector.UnprotectPending(stored)!.ResidenceAddress);

        await service.ConfirmDocumentAsync(Confirmation(front.VerificationId!.Value), default);

        OwnerIdentityData pending = identityProtector.UnprotectPending(stored)!;
        Assert.Equal("Địa chỉ thử nghiệm", pending.ResidenceAddress);
        // Các trường còn lại vẫn lấy từ OCR, bản khai không ghi đè.
        Assert.Equal("079200012345", pending.CitizenId);
        Assert.False(stored.EnrollmentIdentityEncrypted!.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("thử nghiệm")) >= 0);
    }

    [Fact]
    public async Task Confirm_AcceptsEditedDeclarationWithoutReplacingImageEvidence()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, userId);
        client.Reading = Reading("079200099999", "Sai OCR", "2000-02-01");
        EkycDocumentResponse front = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", turn.Id, default);
        EkycStatusResponse result = await service.ConfirmDocumentAsync(Confirmation(front.VerificationId!.Value, "BENEFICIARY_CLAIM", turn.Id), default);
        Assert.Equal("WRONG_PERSON", result.IdentityMatch);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal("9999", result.CitizenIdLast4);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Confirm_RejectsDeclarationNotMatchingBeneficiary()
    {
        Consent(EkycPurpose.BeneficiaryClaim, Now, EkycRules.NoticeVersion);
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, userId);
        EkycDocumentResponse front = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", turn.Id, default);
        EkycDocumentConfirmationRequest request = Confirmation(front.VerificationId!.Value, "BENEFICIARY_CLAIM", turn.Id);
        request.FullName = "Người khác";
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ConfirmDocumentAsync(request, default));
        Assert.Equal(ErrorCode.ValidationError, error.Code);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("purpose")]
    [InlineData("turn")]
    [InlineData("other-user")]
    [InlineData("expired")]
    public async Task Confirm_RejectsForeignStaleExpiredOrWrongContext(string scenario)
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        EkycDocumentResponse front = await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, default);
        EkycDocumentConfirmationRequest request = Confirmation(front.VerificationId!.Value);
        EkycVerification verification = Assert.Single(repository.Verifications);
        if (scenario == "stale") request.VerificationId = Guid.NewGuid();
        if (scenario == "purpose") request.Purpose = "OWNER_ALIVE";
        if (scenario == "turn") request.HandoverTurnId = Guid.NewGuid();
        if (scenario == "other-user") verification.UserId = Guid.NewGuid();
        if (scenario == "expired") verification.DocumentTokenExpiresAt = Now;
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ConfirmDocumentAsync(request, default));
        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("address")]
    [InlineData("future")]
    [InlineData("missing-date")]
    public async Task Confirm_ValidatesDeclarationEvenWithoutController(string field)
    {
        EkycDocumentConfirmationRequest request = Confirmation(Guid.NewGuid());
        if (field == "id") request.IdNumber = "１２３４５６７８９０１２";
        if (field == "name") request.FullName = "  ";
        if (field == "address") request.ResidenceAddress = new string('x', 1001);
        if (field == "future") request.DateOfBirth = new DateOnly(2027, 1, 1);
        if (field == "missing-date") request.DateOfBirth = null;
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ConfirmDocumentAsync(request, default));
        Assert.Equal(ErrorCode.ValidationError, error.Code);
    }

    [Fact]
    public async Task Back_DifferentCard_RejectedAndNotChecked()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        client.BackReading = new EkycBackReading { IdNumber = "079200019999" };

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitBackAsync(
            Image(), "back.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentMismatch, error.Code);
        Assert.False(Assert.Single(repository.Verifications).BackSideChecked);
    }

    [Fact]
    public async Task Back_NoMrz_NotConclusiveButNotAnError()
    {
        Consent(EkycPurpose.OwnerEnrollment, Now, EkycRules.NoticeVersion);
        await service.SubmitDocumentAsync(Image(), "front.jpg", "image/jpeg", null, CancellationToken.None);
        client.BackReading = new EkycBackReading { IdNumber = string.Empty };

        EkycStatusResponse response = await service.SubmitBackAsync(
            Image(), "back.jpg", "image/jpeg", CancellationToken.None);

        Assert.False(response.BackSideChecked);
        Assert.Equal("PENDING", response.Status);
        // Thẻ cũ không có MRZ vẫn tính là đã gửi mặt sau, để bước camera không bị chặn.
        Assert.Equal(Now, Assert.Single(repository.Verifications).BackSubmittedAt);
    }

    [Fact]
    public async Task Back_WithoutFront_Conflict()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.SubmitBackAsync(
            Image(), "back.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Theory]
    [InlineData("079200012345", "2345", true)]
    [InlineData("079200012345", "9999", false)]
    [InlineData("", "2345", false)]
    [InlineData("079200012345", "", false)]
    public void IsSameCard_ComparesLastFourDigits(string backId, string frontLast4, bool expected)
    {
        Assert.Equal(expected, EkycRules.IsSameCard(backId, frontLast4));
    }
}
