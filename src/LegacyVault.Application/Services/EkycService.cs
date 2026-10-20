using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class EkycService : IEkycService
{
    private readonly ICurrentUser currentUser;
    private readonly IEkycVerificationRepository verificationRepository;
    private readonly IEkycConsentRepository consentRepository;
    private readonly IHandoverRepository handoverRepository;
    private readonly IBeneficiaryRepository beneficiaryRepository;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly IOwnerFaceTemplateRepository faceTemplateRepository;
    private readonly IFaceTemplateProtector faceTemplateProtector;
    private readonly IOwnerIdentityRepository identityRepository;
    private readonly IOwnerIdentityProtector identityProtector;
    private readonly IEkycClient ekycClient;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public EkycService(
        ICurrentUser currentUser,
        IEkycVerificationRepository verificationRepository,
        IEkycConsentRepository consentRepository,
        IHandoverRepository handoverRepository,
        IBeneficiaryRepository beneficiaryRepository,
        IBeneficiaryDataProtector beneficiaryProtector,
        IOwnerFaceTemplateRepository faceTemplateRepository,
        IFaceTemplateProtector faceTemplateProtector,
        IOwnerIdentityRepository identityRepository,
        IOwnerIdentityProtector identityProtector,
        IEkycClient ekycClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.verificationRepository = verificationRepository;
        this.consentRepository = consentRepository;
        this.handoverRepository = handoverRepository;
        this.beneficiaryRepository = beneficiaryRepository;
        this.beneficiaryProtector = beneficiaryProtector;
        this.faceTemplateRepository = faceTemplateRepository;
        this.faceTemplateProtector = faceTemplateProtector;
        this.identityRepository = identityRepository;
        this.identityProtector = identityProtector;
        this.ekycClient = ekycClient;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<EkycConsentResponse> RecordConsentAsync(EkycConsentRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycPurpose purpose = ParsePurpose(request.Purpose);
        if (request.NoticeVersion != EkycRules.NoticeVersion)
        {
            throw ValidationErrors.For("noticeVersion", "Thông báo đã thay đổi, vui lòng đọc lại rồi đồng ý.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        EkycConsent consent = new EkycConsent();
        consent.UserId = userId;
        consent.Purpose = purpose;
        consent.NoticeVersion = EkycRules.NoticeVersion;
        consent.ConsentedAt = now;
        consentRepository.Add(consent);
        await unitOfWork.SaveChangesAsync(ct);

        EkycConsentResponse response = new EkycConsentResponse();
        response.Purpose = purpose.ToApiString();
        response.NoticeVersion = consent.NoticeVersion;
        response.ConsentedAt = now;
        response.ExpiresAt = now + EkycRules.ConsentLifetime;
        return response;
    }

    public async Task<EkycDocumentResponse> SubmitDocumentAsync(
        Stream content, string fileName, string contentType, Guid? handoverTurnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        // EKYC-03: phải có đồng ý còn hiệu lực trước khi nhận bất kỳ ảnh nào.
        EkycConsent? consent = await consentRepository.FindLatestByUserIdAsync(userId, ct);
        if (consent == null || !EkycRules.IsConsentValid(consent, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycConsentRequired,
                "Vui lòng đọc thông báo và đồng ý trước khi chụp giấy tờ.");
        }
        EkycPurpose purpose = consent.Purpose;

        // EKYC-02: người được chỉ định lấy từ backend (lượt bàn giao đã ghim), không nhận từ FE.
        BeneficiaryPersonalData? expected = await LoadExpectedPersonAsync(purpose, handoverTurnId, userId, ct);

        byte[] image = await ReadImageAsync(content, contentType, ct);

        // Đọc trước, hủy lượt cũ sau: ảnh lỗi thì lượt Pending đang có vẫn giữ nguyên.
        EkycDocumentReading reading = await ekycClient.ReadFrontAsync(image, fileName, contentType, ct);

        // EKYC-24, EKYC-27: Owner đăng ký phải đọc được đủ CCCD, họ tên, ngày sinh, và đã gắn danh tính thì phải ra đúng danh tính đó.
        OwnerIdentityData? enrollmentIdentity = null;
        if (purpose == EkycPurpose.OwnerEnrollment && reading.DocumentToken != null)
        {
            enrollmentIdentity = await CheckEnrollmentIdentityAsync(userId, reading, ct);
        }

        EkycIdentityMatch identityMatch = EkycIdentityMatch.NotChecked;
        if (expected != null)
        {
            identityMatch = EkycRules.CompareIdentity(reading, expected);
        }

        EkycVerification? previous = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        if (previous != null && previous.DocumentToken != null)
        {
            await TryDeleteDocumentAsync(previous.DocumentToken, ct);
            previous.DocumentToken = null;
            previous.DocumentTokenExpiresAt = null;
        }
        if (previous != null)
        {
            ClearPendingIdentity(previous);
        }

        EkycDocumentResponse response = new EkycDocumentResponse
        {
            IdNumber = reading.IdNumber,
            FullName = reading.FullName,
            DateOfBirth = reading.DateOfBirth,
            ResidenceAddress = reading.ResidenceAddress,
            IdentityMatch = identityMatch.ToApiString(),
            Warnings = reading.Warnings,
        };

        if (reading.DocumentToken == null)
        {
            // Chưa tìm được đúng một khuôn mặt: vẫn trả chữ đọc được, nhưng chưa mở bước so khớp.
            response.CanMatchFace = false;
            await unitOfWork.SaveChangesAsync(ct);
            return response;
        }

        EkycVerification verification = new EkycVerification();
        verification.UserId = userId;
        verification.Status = EkycStatus.Pending;
        verification.Purpose = purpose;
        verification.HandoverTurnId = handoverTurnId;
        verification.IdentityMatch = identityMatch;
        verification.CitizenIdLast4 = EkycRules.GetCitizenIdLast4(reading.IdNumber);
        verification.DocumentToken = reading.DocumentToken;
        verification.DocumentTokenExpiresAt = now + EkycRules.DocumentTokenLifetime;
        verification.CreatedAt = now;
        if (enrollmentIdentity != null)
        {
            identityProtector.ProtectPending(verification, enrollmentIdentity);
        }
        verificationRepository.Add(verification);
        await unitOfWork.SaveChangesAsync(ct);

        response.VerificationId = verification.Id;
        response.CanMatchFace = true;
        response.ExpiresAt = verification.DocumentTokenExpiresAt;
        return response;
    }

    public async Task<EkycStatusResponse> ConfirmDocumentAsync(EkycDocumentConfirmationRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<ValidationResult> errors = new();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
        {
            throw ValidationErrors.For("document", "Kiểm tra lại thông tin giấy tờ đã nhập.");
        }
        if (request.DateOfBirth > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
        {
            throw ValidationErrors.For("dateOfBirth", "Ngày sinh không được ở tương lai.");
        }

        EkycPurpose purpose = ParsePurpose(request.Purpose);
        EkycVerification verification = await RequirePendingVerificationAsync(userId, ct);
        if (verification.Id != request.VerificationId || verification.Purpose != purpose ||
            verification.HandoverTurnId != request.HandoverTurnId)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                "Lượt xác minh đã thay đổi. Vui lòng đọc lại mặt trước giấy tờ.");
        }

        BeneficiaryPersonalData? expected = await LoadExpectedPersonAsync(purpose, request.HandoverTurnId, userId, ct);
        if (expected != null)
        {
            EkycDocumentReading declaration = new()
            {
                IdNumber = request.IdNumber,
                FullName = request.FullName.Trim(),
                DateOfBirth = request.DateOfBirth!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            if (EkycRules.CompareIdentity(declaration, expected) != EkycIdentityMatch.Matched)
            {
                throw ValidationErrors.For("document", "Thông tin xác nhận chưa khớp hồ sơ người nhận. Kiểm tra lại giấy tờ.");
            }
        }

        // Bản tự khai chỉ được kiểm tra trong request, không lưu PII hoặc nâng kết quả từ ảnh thành MATCHED.
        return verification.ToResponse();
    }

    public async Task<EkycBackResponse> SubmitBackAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        // EKYC-04: mặt sau thuộc cùng lần chụp với mặt trước, nên phải có lượt đang chờ selfie.
        EkycVerification? verification = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        if (verification == null || !EkycRules.CanMatchFace(verification, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                "Vui lòng đọc mặt trước giấy tờ trước khi gửi mặt sau.");
        }

        byte[] image = await ReadImageAsync(content, contentType, ct);
        EkycBackReading reading = await ekycClient.ReadBackAsync(image, fileName, contentType, ct);

        if (reading.IdNumber.Length > 0 && !EkycRules.IsSameCard(reading.IdNumber, verification.CitizenIdLast4))
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.EkycDocumentMismatch,
                "Hai mặt giấy tờ không cùng một thẻ. Vui lòng chụp lại.");
        }

        // Thẻ không có MRZ chứa số căn cước thì không kết luận được: giữ false, không coi là lỗi.
        verification.BackSideChecked = reading.IdNumber.Length > 0;
        await unitOfWork.SaveChangesAsync(ct);

        // BR-88: nơi cư trú đọc từ mặt sau chỉ trả cho người dùng đối chiếu; không gán vào lượt eKYC nên không lưu.
        return verification.ToBackResponse(reading.ResidenceAddress);
    }

    public async Task<EkycStatusResponse> SubmitSelfieAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        EkycVerification? verification = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        if (verification == null || !EkycRules.CanMatchFace(verification, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                "Vui lòng đọc lại mặt trước giấy tờ trước khi gửi ảnh chân dung.");
        }
        string documentToken = verification.DocumentToken!;

        // Đường ảnh selfie tĩnh cũng phải qua thử thách camera trước (EKYC-14): chưa qua thì chưa so khớp.
        if (!verification.LivenessPassed)
        {
            if (verification.LivenessSessionId == null)
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.EkycLivenessRequired,
                    "Vui lòng hoàn thành thử thách camera trước khi gửi ảnh chân dung.");
            }
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycLivenessNotPassed,
                "Bạn chưa hoàn thành thử thách camera.");
        }

        byte[] selfie = await ReadImageAsync(content, contentType, ct);

        EkycSpoofCheck spoofCheck = await ekycClient.CheckSpoofAsync(selfie, fileName, contentType, ct);
        verification.SpoofCheckPassed = spoofCheck.Passed;
        if (!spoofCheck.Passed)
        {
            await FinishAsync(verification, EkycStatus.Failed, now, ct);
            return verification.ToResponse();
        }

        EkycFaceComparison comparison = await ekycClient.CompareFaceAsync(
            documentToken, selfie, fileName, contentType, EkycRules.MatchThreshold, ct);
        verification.FaceMatchScore = comparison.Score;

        // Tự so lại với ngưỡng của server, không tin cờ matched do dịch vụ ngoài trả về.
        if (comparison.Score >= EkycRules.MatchThreshold)
        {
            await FinishAsync(verification, EkycStatus.Passed, now, ct);
        }
        else
        {
            await FinishAsync(verification, EkycStatus.Failed, now, ct);
        }
        return verification.ToResponse();
    }

    public async Task<EkycLivenessResponse> StartLivenessAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycVerification verification = await RequirePendingVerificationAsync(userId, ct);

        // Gọi lại thì hủy phiên cũ, lỗi hủy bỏ qua vì dịch vụ tự dọn phiên hết hạn.
        string? previousSession = verification.LivenessSessionId;
        EkycLivenessState state = await ekycClient.StartLivenessAsync(ct);
        verification.LivenessSessionId = state.SessionId;
        await unitOfWork.SaveChangesAsync(ct);

        if (previousSession != null)
        {
            await TryDeleteLivenessAsync(previousSession, ct);
        }
        return state.ToResponse(true);
    }

    public async Task<EkycLivenessResponse> SubmitLivenessFrameAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycVerification verification = await RequirePendingVerificationAsync(userId, ct);
        string sessionId = RequireLivenessSession(verification);

        byte[] frame = await ReadImageAsync(content, contentType, ct);
        EkycLivenessState state = await ekycClient.SendLivenessFrameAsync(sessionId, frame, fileName, contentType, ct);

        // Ghi nhớ lúc phiên đạt cả động tác và chống giả mạo để đường ảnh selfie tĩnh cũng dùng được (EKYC-14).
        if (state.Passed && !verification.LivenessPassed)
        {
            verification.LivenessPassed = true;
            await unitOfWork.SaveChangesAsync(ct);
        }
        return state.ToResponse(false);
    }

    public async Task<EkycStatusResponse> CompleteLivenessAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();
        EkycVerification verification = await RequirePendingVerificationAsync(userId, ct);
        string sessionId = RequireLivenessSession(verification);
        string documentToken = verification.DocumentToken!;

        // EKYC-13: selfie lấy từ chính phiên đã đạt. Phiên chưa đạt thì dịch vụ từ chối (EKYC_LIVENESS_NOT_PASSED).
        byte[] selfie = await ekycClient.GetLivenessSelfieAsync(sessionId, ct);

        // Dịch vụ chỉ trả selfie khi cả động tác và chống giả mạo trong phiên đều đạt (EKYC-14).
        verification.SpoofCheckPassed = true;
        verification.LivenessPassed = true;

        EkycFaceComparison comparison = await ekycClient.CompareFaceAsync(
            documentToken, selfie, "liveness.jpg", "image/jpeg", EkycRules.MatchThreshold, ct);
        verification.FaceMatchScore = comparison.Score;
        verification.LivenessSessionId = null;

        // Tự so lại với ngưỡng của server, không tin cờ matched do dịch vụ ngoài trả về.
        if (comparison.Score >= EkycRules.MatchThreshold)
        {
            // EKYC-22: Owner đăng ký đạt thì tạo mẫu khuôn mặt từ chính selfie của phiên này, lưu cùng lần chốt kết quả.
            // Lấy vector trước khi chốt: dịch vụ lỗi thì lượt vẫn Pending để thử lại, không có lượt đạt mà thiếu mẫu.
            if (verification.Purpose == EkycPurpose.OwnerEnrollment)
            {
                await AddFaceTemplateAsync(userId, verification.Id, selfie, now, ct);
            }
            await FinishAsync(verification, EkycStatus.Passed, now, ct);
        }
        else
        {
            await FinishAsync(verification, EkycStatus.Failed, now, ct);
        }
        await TryDeleteLivenessAsync(sessionId, ct);
        return verification.ToResponse();
    }

    /// <summary>
    /// Tạo mẫu khuôn mặt Owner từ selfie của phiên thử thách đã đạt (EKYC-22). Đã có mẫu thì giữ nguyên, không thay bằng
    /// mẫu mới (EKYC-23). Chỉ đánh dấu INSERT; lưu cùng lần FinishAsync.
    /// </summary>
    private async Task AddFaceTemplateAsync(
        Guid userId, Guid verificationId, byte[] selfie, DateTimeOffset now, CancellationToken ct)
    {
        OwnerFaceTemplate? existing = await faceTemplateRepository.FindLatestByUserIdAsync(userId, ct);
        if (existing != null)
        {
            return;
        }

        EkycFaceEmbedding embedding = await ekycClient.GetFaceEmbeddingAsync(selfie, "liveness.jpg", "image/jpeg", ct);

        OwnerFaceTemplate template = new OwnerFaceTemplate();
        template.UserId = userId;
        template.Version = 1;
        template.ModelName = embedding.ModelName;
        template.SourceVerificationId = verificationId;
        template.CreatedAt = now;
        faceTemplateProtector.Protect(template, embedding.Values);
        faceTemplateRepository.Add(template);
    }

    /// <summary>Lượt eKYC mới nhất phải còn chờ selfie (đã đọc mặt trước, còn trong 30 phút).</summary>
    private async Task<EkycVerification> RequirePendingVerificationAsync(Guid userId, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        EkycVerification? verification = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        if (verification == null || !EkycRules.CanMatchFace(verification, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                "Vui lòng đọc lại mặt trước giấy tờ trước khi làm thử thách camera.");
        }
        return verification;
    }

    private static string RequireLivenessSession(EkycVerification verification)
    {
        if (verification.LivenessSessionId == null)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycLivenessRequired,
                "Vui lòng bắt đầu thử thách camera trước.");
        }
        return verification.LivenessSessionId;
    }

    /// <summary>Hủy phiên thử thách ở dịch vụ eKYC. Lỗi thì bỏ qua vì dịch vụ tự dọn phiên sau 90 giây.</summary>
    private async Task TryDeleteLivenessAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await ekycClient.DeleteLivenessAsync(sessionId, ct);
        }
        catch (AppException)
        {
            // Cố ý bỏ qua: hủy thất bại không ảnh hưởng kết quả, phiên tự hết hạn.
        }
    }

    public async Task<EkycStatusResponse> GetMyStatusAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycVerification? verification = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        if (verification == null)
        {
            return EkycMappings.NotStarted();
        }
        return verification.ToResponse();
    }

    /// <summary>
    /// Lấy dữ liệu người được chỉ định của lượt bàn giao để đối chiếu (BeneficiaryClaim). Mục đích khác thì không có lượt.
    /// Lượt của người khác hoặc không tồn tại đều trả 404 (SEC-04).
    /// </summary>
    private async Task<BeneficiaryPersonalData?> LoadExpectedPersonAsync(
        EkycPurpose purpose, Guid? handoverTurnId, Guid userId, CancellationToken ct)
    {
        if (purpose != EkycPurpose.BeneficiaryClaim)
        {
            if (handoverTurnId != null)
            {
                throw ValidationErrors.For("handoverTurnId", "Chỉ xác minh người nhận mới gửi kèm lượt bàn giao.");
            }
            return null;
        }
        if (handoverTurnId == null)
        {
            throw ValidationErrors.For("handoverTurnId", "Thiếu lượt bàn giao cần xác minh.");
        }

        HandoverTurn? turn = await handoverRepository.FindTurnByIdAsync(handoverTurnId.Value, ct);
        if (turn == null || turn.ClaimantUserId != userId)
        {
            throw HandoverNotFound();
        }
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed,
                "Lượt này không ở bước xác minh danh tính.");
        }

        Beneficiary? beneficiary = await beneficiaryRepository.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary == null)
        {
            throw HandoverNotFound();
        }
        return beneficiaryProtector.Unprotect(beneficiary);
    }

    /// <summary>
    /// Đổi chuỗi UPPER_SNAKE_CASE FE gửi sang enum. Không dùng Enum.Parse thẳng để FE không gửi được số hay tên C#.
    /// </summary>
    private static EkycPurpose ParsePurpose(string purpose)
    {
        foreach (EkycPurpose value in Enum.GetValues<EkycPurpose>())
        {
            if (string.Equals(value.ToApiString(), purpose, StringComparison.Ordinal))
            {
                return value;
            }
        }
        throw ValidationErrors.For("purpose", "Mục đích xác minh không hợp lệ.");
    }

    private static AppException HandoverNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound,
            "Không tìm thấy nội dung bàn giao.");
    }

    /// <summary>
    /// Đọc danh tính từ mặt trước của Owner đăng ký và đối chiếu với danh tính đã gắn nếu có (EKYC-24).
    /// Đọc chưa đủ hoặc OCR chưa chắc: 400 EKYC_IMAGE_REJECTED, chụp lại. CCCD khác rõ ràng: 409 EKYC_IDENTITY_MISMATCH.
    /// Lỗi thì hủy khuôn mặt vừa giữ ở dịch vụ eKYC. Trả danh tính đọc được để giữ tạm (mã hóa) trên lượt.
    /// </summary>
    private async Task<OwnerIdentityData> CheckEnrollmentIdentityAsync(
        Guid userId, EkycDocumentReading reading, CancellationToken ct)
    {
        OwnerIdentityData? readIdentity = EkycRules.TryReadOwnerIdentity(reading);
        if (readIdentity == null)
        {
            await TryDeleteDocumentAsync(reading.DocumentToken!, ct);
            throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected,
                "Chưa đọc rõ số CCCD, họ tên hoặc ngày sinh. Vui lòng chụp lại mặt trước rõ hơn.");
        }

        OwnerIdentity? bound = await identityRepository.FindByUserIdAsync(userId, ct);
        if (bound != null)
        {
            OwnerIdentityData expected = identityProtector.Unprotect(bound);
            EkycIdentityMatch match = EkycRules.CompareIdentity(reading, expected);
            if (match == EkycIdentityMatch.WrongPerson)
            {
                await TryDeleteDocumentAsync(reading.DocumentToken!, ct);
                throw new AppException(AppErrorType.Conflict, ErrorCode.EkycIdentityMismatch,
                    "Giấy tờ không khớp với danh tính đã gắn cho tài khoản này.");
            }
            if (match == EkycIdentityMatch.NeedsReview)
            {
                await TryDeleteDocumentAsync(reading.DocumentToken!, ct);
                throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected,
                    "Chưa đọc rõ giấy tờ để đối chiếu với danh tính đã gắn. Vui lòng chụp lại mặt trước rõ hơn.");
            }
        }
        return readIdentity;
    }

    /// <summary>
    /// Gắn danh tính cho Owner khi lượt OWNER_ENROLLMENT đạt (EKYC-27). Đã có danh tính thì giữ nguyên (đã đối chiếu
    /// lúc đọc giấy tờ, EKYC-24). Chỉ đánh dấu INSERT; lưu cùng lần FinishAsync.
    /// </summary>
    private async Task BindIdentityAsync(EkycVerification verification, DateTimeOffset now, CancellationToken ct)
    {
        OwnerIdentity? existing = await identityRepository.FindByUserIdAsync(verification.UserId, ct);
        if (existing != null)
        {
            return;
        }
        OwnerIdentityData? pending = identityProtector.UnprotectPending(verification);
        if (pending == null)
        {
            return;
        }

        OwnerIdentity identity = new OwnerIdentity();
        identity.UserId = verification.UserId;
        identity.SourceVerificationId = verification.Id;
        identity.CreatedAt = now;
        identityProtector.Protect(identity, pending);
        identityRepository.Add(identity);
    }

    /// <summary>Xóa bản tạm của danh tính trên lượt eKYC (đã gắn xong, đã thất bại, hoặc bị lượt mới thay).</summary>
    private static void ClearPendingIdentity(EkycVerification verification)
    {
        verification.EnrollmentIdentityKeyId = null;
        verification.EnrollmentWrappedDataKey = null;
        verification.EnrollmentIdentityEncrypted = null;
    }

    /// <summary>Chốt kết quả, xóa mã tham chiếu ở cả DB và dịch vụ eKYC để mỗi lượt chỉ so khớp một lần.</summary>
    private async Task FinishAsync(EkycVerification verification, EkycStatus status, DateTimeOffset now, CancellationToken ct)
    {
        if (status == EkycStatus.Passed && verification.Purpose == EkycPurpose.OwnerEnrollment)
        {
            await BindIdentityAsync(verification, now, ct);
        }
        ClearPendingIdentity(verification);

        string? documentToken = verification.DocumentToken;
        verification.Status = status;
        verification.CompletedAt = now;
        verification.DocumentToken = null;
        verification.DocumentTokenExpiresAt = null;
        await unitOfWork.SaveChangesAsync(ct);

        if (documentToken != null)
        {
            await TryDeleteDocumentAsync(documentToken, ct);
        }
    }

    /// <summary>Xóa khuôn mặt ở dịch vụ eKYC. Lỗi thì bỏ qua vì dịch vụ tự hủy mã sau 30 phút.</summary>
    private async Task TryDeleteDocumentAsync(string documentToken, CancellationToken ct)
    {
        try
        {
            await ekycClient.DeleteDocumentAsync(documentToken, ct);
        }
        catch (AppException)
        {
            // Cố ý bỏ qua: xóa thất bại không ảnh hưởng kết quả, mã tham chiếu tự hết hạn.
        }
    }

    private static async Task<byte[]> ReadImageAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!EkycRules.IsAllowedContentType(contentType))
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected,
                "Chỉ nhận ảnh JPEG hoặc PNG.");
        }

        using (MemoryStream buffer = new MemoryStream())
        {
            byte[] chunk = new byte[81920];
            int read = await content.ReadAsync(chunk, 0, chunk.Length, ct);
            while (read > 0)
            {
                if (buffer.Length + read > EkycRules.MaxImageBytes)
                {
                    throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge,
                        "Ảnh vượt quá 8 MiB.");
                }
                buffer.Write(chunk, 0, read);
                read = await content.ReadAsync(chunk, 0, chunk.Length, ct);
            }

            if (buffer.Length == 0)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected, "Ảnh rỗng.");
            }
            return buffer.ToArray();
        }
    }
}
