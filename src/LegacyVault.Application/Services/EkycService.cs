using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// BR-48 sửa 25/10/2026: ảnh mặt trước CCCD của lượt OWNER_ENROLLMENT được lưu MÃ HÓA (kho lưu trữ private) để Verifier
/// đối chiếu khi xét hồ sơ chứng tử. Lượt đạt thì chuyển sang danh tính đã gắn; lượt thất bại hoặc bị lượt mới thay thì xóa.
/// </remarks>
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
    private readonly IAssetContentStore contentStore;
    private readonly IEkycOptions ekycOptions;

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
        TimeProvider timeProvider,
        IAssetContentStore contentStore,
        IEkycOptions ekycOptions)
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
        this.contentStore = contentStore;
        this.ekycOptions = ekycOptions;
    }

    public async Task<EkycConsentResponse> RecordConsentAsync(EkycConsentRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycPurpose purpose = ParsePurpose(request.Purpose);
        await RequireNotVerifiedAsync(userId, purpose, ct);
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
        await RequireNotVerifiedAsync(userId, purpose, ct);

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
        string? discardedImageKey = null;
        if (previous != null)
        {
            ClearPendingIdentity(previous);
            discardedImageKey = TakePendingDocumentImage(previous);
        }

        EkycDocumentResponse response = new EkycDocumentResponse
        {
            IdNumber = reading.IdNumber,
            FullName = reading.FullName,
            DateOfBirth = reading.DateOfBirth,
            DateOfExpiry = reading.DateOfExpiry,
            ResidenceAddress = reading.ResidenceAddress,
            IdentityMatch = identityMatch.ToApiString(),
            Warnings = reading.Warnings,
        };

        if (reading.DocumentToken == null)
        {
            // Chưa tìm được đúng một khuôn mặt: vẫn trả chữ đọc được, nhưng chưa mở bước so khớp.
            response.CanMatchFace = false;
            await unitOfWork.SaveChangesAsync(ct);
            await IdentityImageStorage.TryDeleteAsync(contentStore, discardedImageKey);
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
        string? savedImageKey = null;
        if (enrollmentIdentity != null)
        {
            identityProtector.ProtectPending(verification, enrollmentIdentity);
            savedImageKey = await KeepPendingDocumentImageAsync(verification, image, ct);
        }
        verificationRepository.Add(verification);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Bù trừ: không để lại ảnh không có bản ghi. Không dùng ct vì request có thể đã bị hủy.
            await IdentityImageStorage.TryDeleteAsync(contentStore, savedImageKey);
            throw;
        }
        await IdentityImageStorage.TryDeleteAsync(contentStore, discardedImageKey);

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
        return verification.ToResponse(ekycOptions.CurrentThreshold);
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
        verification.BackSubmittedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        // BR-88: nơi cư trú đọc từ mặt sau chỉ trả cho người dùng đối chiếu; không gán vào lượt eKYC nên không lưu.
        return verification.ToBackResponse(reading.ResidenceAddress, ekycOptions.CurrentThreshold);
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
        RequireBackSide(verification);

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
        double threshold = ekycOptions.CurrentThreshold;

        EkycSpoofCheck spoofCheck = await ekycClient.CheckSpoofAsync(selfie, fileName, contentType, ct);
        verification.SpoofCheckPassed = spoofCheck.Passed;
        if (!spoofCheck.Passed)
        {
            await FinishAsync(verification, EkycStatus.Failed, now, ct);
            return verification.ToResponse(threshold);
        }

        EkycFaceComparison comparison = await ekycClient.CompareFaceAsync(
            documentToken, selfie, fileName, contentType, threshold, ct);
        verification.FaceMatchScore = comparison.Score;

        // Tự so lại với ngưỡng của server, không tin cờ matched do dịch vụ ngoài trả về.
        if (comparison.Score >= threshold)
        {
            await FinishAsync(verification, EkycStatus.Passed, now, ct);
        }
        else
        {
            await FinishAsync(verification, EkycStatus.Failed, now, ct);
        }
        EkycStatusResponse response = verification.ToResponse(threshold);
        response.ReferenceCrop = comparison.ReferenceCrop;
        response.SelfieCrop = comparison.SelfieCrop;
        return response;
    }

    public async Task<EkycLivenessResponse> StartLivenessAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycVerification verification = await RequirePendingVerificationAsync(userId, ct);
        RequireBackSide(verification);

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
        RequireBackSide(verification);
        string sessionId = RequireLivenessSession(verification);
        string documentToken = verification.DocumentToken!;

        // EKYC-13: selfie lấy từ chính phiên đã đạt. Phiên chưa đạt thì dịch vụ từ chối (EKYC_LIVENESS_NOT_PASSED).
        byte[] selfie = await ekycClient.GetLivenessSelfieAsync(sessionId, ct);

        // Dịch vụ chỉ trả selfie khi cả động tác và chống giả mạo trong phiên đều đạt (EKYC-14).
        verification.SpoofCheckPassed = true;
        verification.LivenessPassed = true;
        double threshold = ekycOptions.CurrentThreshold;

        EkycFaceComparison comparison = await ekycClient.CompareFaceAsync(
            documentToken, selfie, "liveness.jpg", "image/jpeg", threshold, ct);
        verification.FaceMatchScore = comparison.Score;
        verification.LivenessSessionId = null;

        // Tự so lại với ngưỡng của server, không tin cờ matched do dịch vụ ngoài trả về.
        if (comparison.Score >= threshold)
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
        EkycStatusResponse response = verification.ToResponse(threshold);
        response.ReferenceCrop = comparison.ReferenceCrop;
        response.SelfieCrop = comparison.SelfieCrop;
        return response;
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

    /// <summary>
    /// Owner đã có lượt OWNER_ENROLLMENT mới nhất đạt thì khóa, không cho xác minh lại. Chưa đạt (chưa làm, đang chờ,
    /// thất bại) thì cho tiếp tục. Các mục đích khác không khóa ở đây (người nhận bị khóa bởi trạng thái lượt bàn giao).
    /// </summary>
    private async Task RequireNotVerifiedAsync(Guid userId, EkycPurpose purpose, CancellationToken ct)
    {
        if (purpose != EkycPurpose.OwnerEnrollment)
        {
            return;
        }
        EkycVerification? latest = await verificationRepository.FindLatestByUserAndPurposeAsync(userId, purpose, ct);
        if (latest != null && latest.Status == EkycStatus.Passed)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycAlreadyVerified,
                "Danh tính đã được xác minh, không cần xác minh lại.");
        }
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

    /// <summary>Mặt sau là bắt buộc trước thử thách camera và so khớp selfie; chưa gửi thì 409 EKYC_BACK_REQUIRED.</summary>
    private static void RequireBackSide(EkycVerification verification)
    {
        if (verification.BackSubmittedAt == null)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycBackRequired,
                "Vui lòng chụp và gửi mặt sau giấy tờ trước khi tiếp tục.");
        }
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

    public Task<EkycStatusResponse> GetMyStatusAsync(CancellationToken ct)
    {
        return GetMyStatusAsync(null, ct);
    }

    public async Task<EkycStatusResponse> GetMyStatusAsync(string? purpose, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycVerification? verification;
        if (purpose == null)
        {
            verification = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        }
        else
        {
            EkycPurpose parsed = ParsePurpose(purpose);
            verification = null;
            if (parsed == EkycPurpose.OwnerEnrollment)
            {
                // Owner chỉ xác minh một lần: đã đạt thì giữ kết quả đó, không để lượt chụp lại đè lên.
                verification = await verificationRepository.FindLatestPassedByUserAndPurposeAsync(userId, parsed, ct);
            }
            if (verification == null)
            {
                verification = await verificationRepository.FindLatestByUserAndPurposeAsync(userId, parsed, ct);
            }
        }
        if (verification == null)
        {
            return EkycMappings.NotStarted(ekycOptions.CurrentThreshold);
        }
        return verification.ToResponse(ekycOptions.CurrentThreshold);
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
        if (bound == null)
        {
            // Lần gắn danh tính đầu tiên: CCCD của Chủ kho không được trùng người nhận đã thêm trước đó.
            await EnsureNotAmongBeneficiariesAsync(userId, readIdentity.CitizenId, reading.DocumentToken!, ct);
            // CCCD của Chủ kho là duy nhất trên toàn hệ thống, không được trùng với tài khoản khác đã gắn danh tính.
            await EnsureCitizenIdNotUsedByAnotherUserAsync(userId, readIdentity.CitizenId, reading.DocumentToken!, ct);
        }
        if (bound != null)
        {
            OwnerIdentityData expected = identityProtector.Unprotect(bound);
            EkycIdentityMatch match = EkycRules.CompareIdentity(reading, expected);
            if (match == EkycIdentityMatch.WrongPerson)
            {
                await TryDeleteDocumentAsync(reading.DocumentToken!, ct);
                throw new AppException(AppErrorType.Conflict, ErrorCode.EkycIdentityMismatch,
                    "Giấy tờ không khớp với danh tính đã gắn cho tài khoản này.",
                    new Dictionary<string, object?> { ["reason"] = "WRONG_PERSON" });
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
    /// Số CCCD của Chủ kho phải là duy nhất trên toàn hệ thống (mỗi công dân chỉ gắn với một tài khoản).
    /// </summary>
    private async Task EnsureCitizenIdNotUsedByAnotherUserAsync(
        Guid userId, string citizenId, string? documentToken, CancellationToken ct)
    {
        string last4 = citizenId.Substring(citizenId.Length - 4);
        List<OwnerIdentity> candidates = await identityRepository.ListByCitizenIdLast4ExceptUserAsync(last4, userId, ct);
        foreach (OwnerIdentity candidate in candidates)
        {
            OwnerIdentityData data = identityProtector.Unprotect(candidate);
            if (string.Equals(data.CitizenId, citizenId, StringComparison.Ordinal))
            {
                if (documentToken != null)
                {
                    await TryDeleteDocumentAsync(documentToken, ct);
                }
                throw new AppException(AppErrorType.Conflict, ErrorCode.EkycIdentityMismatch,
                    "Số CCCD này đã được sử dụng để xác minh cho một tài khoản khác trong hệ thống.",
                    new Dictionary<string, object?> { ["reason"] = "CITIZEN_ID_ALREADY_USED" });
            }
        }
    }

    /// <summary>
    /// Chủ kho chưa gắn danh tính nên lúc thêm người nhận chưa so được CCCD. Đối chiếu ngược khi gắn: nếu CCCD trên giấy tờ
    /// trùng một người nhận trong kho của chính Owner thì từ chối và hủy khuôn mặt vừa giữ.
    /// </summary>
    private async Task EnsureNotAmongBeneficiariesAsync(
        Guid userId, string citizenId, string documentToken, CancellationToken ct)
    {
        string last4 = citizenId.Substring(citizenId.Length - 4);
        List<Beneficiary> candidates = await beneficiaryRepository.ListByOwnerAndCitizenIdLast4Async(userId, last4, ct);
        foreach (Beneficiary candidate in candidates)
        {
            BeneficiaryPersonalData data = beneficiaryProtector.Unprotect(candidate);
            if (string.Equals(data.CitizenId, citizenId, StringComparison.Ordinal))
            {
                await TryDeleteDocumentAsync(documentToken, ct);
                throw new AppException(AppErrorType.Conflict, ErrorCode.EkycIdentityMismatch,
                    "Số CCCD trên giấy tờ trùng với một người nhận bạn đã thêm. Người nhận không được trùng với Chủ kho; hãy xóa hoặc sửa hồ sơ người nhận đó trước.",
                    new Dictionary<string, object?> { ["reason"] = "BENEFICIARY_CONFLICT" });
            }
        }
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
            // Danh tính gắn trước khi có ảnh (BR-48 sửa): lượt đạt mới bổ sung ảnh, không thay ảnh đã có.
            if (existing.DocumentImageStorageKey == null)
            {
                MovePendingDocumentImage(verification, existing);
            }
            return;
        }
        OwnerIdentityData? pending = identityProtector.UnprotectPending(verification);
        if (pending == null)
        {
            return;
        }

        await EnsureCitizenIdNotUsedByAnotherUserAsync(verification.UserId, pending.CitizenId, null, ct);

        OwnerIdentity identity = new OwnerIdentity();
        identity.UserId = verification.UserId;
        identity.SourceVerificationId = verification.Id;
        identity.CreatedAt = now;
        identityProtector.Protect(identity, pending);
        MovePendingDocumentImage(verification, identity);
        identityRepository.Add(identity);
    }

    /// <summary>Xóa bản tạm của danh tính trên lượt eKYC (đã gắn xong, đã thất bại, hoặc bị lượt mới thay).</summary>
    private static void ClearPendingIdentity(EkycVerification verification)
    {
        verification.EnrollmentIdentityKeyId = null;
        verification.EnrollmentWrappedDataKey = null;
        verification.EnrollmentIdentityEncrypted = null;
    }

    /// <summary>
    /// Lưu MÃ HÓA ảnh mặt trước CCCD tạm trên lượt OWNER_ENROLLMENT (BR-48 sửa). Ảnh không phải JPEG/PNG/WebP theo nội
    /// dung thật thì không lưu (eKYC vẫn chạy). Trả khóa lưu trữ để bù trừ nếu lưu DB thất bại.
    /// </summary>
    private async Task<string?> KeepPendingDocumentImageAsync(EkycVerification verification, byte[] image, CancellationToken ct)
    {
        string? contentType = IdentityImageStorage.DetectContentType(image);
        if (contentType == null)
        {
            return null;
        }
        StoredContent stored = await IdentityImageStorage.SaveAsync(contentStore, image, ct);
        verification.PendingDocumentImageStorageKey = stored.StorageKey;
        verification.PendingDocumentImageContentType = contentType;
        verification.PendingDocumentImageSizeBytes = stored.SizeBytes;
        verification.PendingDocumentImageEncryptionKeyId = stored.EncryptionKeyId;
        verification.PendingDocumentImageWrappedDataKey = stored.WrappedDataKey;
        return stored.StorageKey;
    }

    /// <summary>Chuyển tham chiếu ảnh tạm của lượt sang danh tính đã gắn (không chép nội dung), rồi xóa cột tạm.</summary>
    private static void MovePendingDocumentImage(EkycVerification verification, OwnerIdentity identity)
    {
        if (verification.PendingDocumentImageStorageKey == null)
        {
            return;
        }
        identity.DocumentImageStorageKey = verification.PendingDocumentImageStorageKey;
        identity.DocumentImageContentType = verification.PendingDocumentImageContentType;
        identity.DocumentImageSizeBytes = verification.PendingDocumentImageSizeBytes;
        identity.DocumentImageEncryptionKeyId = verification.PendingDocumentImageEncryptionKeyId;
        identity.DocumentImageWrappedDataKey = verification.PendingDocumentImageWrappedDataKey;
        identity.DocumentImageCapturedAt = verification.CreatedAt;
        ClearPendingDocumentImage(verification);
    }

    /// <summary>Bỏ ảnh tạm khỏi lượt; trả khóa lưu trữ để xóa nội dung SAU khi lưu DB. Không có ảnh → null.</summary>
    private static string? TakePendingDocumentImage(EkycVerification verification)
    {
        string? storageKey = verification.PendingDocumentImageStorageKey;
        ClearPendingDocumentImage(verification);
        return storageKey;
    }

    private static void ClearPendingDocumentImage(EkycVerification verification)
    {
        verification.PendingDocumentImageStorageKey = null;
        verification.PendingDocumentImageContentType = null;
        verification.PendingDocumentImageSizeBytes = null;
        verification.PendingDocumentImageEncryptionKeyId = null;
        verification.PendingDocumentImageWrappedDataKey = null;
    }

    /// <summary>
    /// Chốt kết quả, xóa mã tham chiếu ở cả DB và dịch vụ eKYC để mỗi lượt chỉ so khớp một lần. Lượt OWNER_ENROLLMENT
    /// đạt thì ảnh mặt trước đã chuyển sang danh tính; còn lại ảnh tạm bị xóa sau khi lưu DB.
    /// </summary>
    private async Task FinishAsync(EkycVerification verification, EkycStatus status, DateTimeOffset now, CancellationToken ct)
    {
        if (status == EkycStatus.Passed && verification.Purpose == EkycPurpose.OwnerEnrollment)
        {
            await BindIdentityAsync(verification, now, ct);
        }
        ClearPendingIdentity(verification);
        string? discardedImageKey = TakePendingDocumentImage(verification);

        string? documentToken = verification.DocumentToken;
        verification.Status = status;
        verification.CompletedAt = now;
        verification.DocumentToken = null;
        verification.DocumentTokenExpiresAt = null;
        await unitOfWork.SaveChangesAsync(ct);
        await IdentityImageStorage.TryDeleteAsync(contentStore, discardedImageKey);

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
