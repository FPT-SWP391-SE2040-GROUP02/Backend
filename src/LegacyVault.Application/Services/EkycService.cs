using System.ComponentModel.DataAnnotations;
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
/// Ảnh mặt trước CCCD của lượt OWNER_ENROLLMENT được lưu MÃ HÓA (kho lưu trữ private) để Verifier
/// đối chiếu khi xét hồ sơ chứng tử. Lượt đạt thì chuyển sang danh tính đã gắn; lượt thất bại hoặc bị lượt mới thay thì xóa.
/// </remarks>
public sealed class EkycService : IEkycService
{
    private readonly ICurrentUser currentUser;
    private readonly IEkycVerificationRepository verificationRepository;
    private readonly IEkycConsentRepository consentRepository;
    private readonly IBeneficiaryRepository beneficiaryRepository;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly IOwnerIdentityRepository identityRepository;
    private readonly IOwnerIdentityProtector identityProtector;
    private readonly IEkycClient ekycClient;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IAssetContentStore contentStore;
    private readonly IEkycOptions ekycOptions;
    private readonly IAuditLogRepository? auditLogRepository;
    private readonly ICitizenIdBlindIndex? citizenIdIndex;

    public EkycService(
        ICurrentUser currentUser,
        IEkycVerificationRepository verificationRepository,
        IEkycConsentRepository consentRepository,
        IBeneficiaryRepository beneficiaryRepository,
        IBeneficiaryDataProtector beneficiaryProtector,
        IOwnerIdentityRepository identityRepository,
        IOwnerIdentityProtector identityProtector,
        IEkycClient ekycClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IAssetContentStore contentStore,
        IEkycOptions ekycOptions,
        IAuditLogRepository? auditLogRepository = null,
        ICitizenIdBlindIndex? citizenIdIndex = null)
    {
        this.citizenIdIndex = citizenIdIndex;
        this.auditLogRepository = auditLogRepository;
        this.currentUser = currentUser;
        this.verificationRepository = verificationRepository;
        this.consentRepository = consentRepository;
        this.beneficiaryRepository = beneficiaryRepository;
        this.beneficiaryProtector = beneficiaryProtector;
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
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        // phải có đồng ý còn hiệu lực trước khi nhận bất kỳ ảnh nào.
        EkycConsent? consent = await consentRepository.FindLatestByUserIdAsync(userId, ct);
        if (consent == null || !EkycRules.IsConsentValid(consent, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycConsentRequired,
                "Vui lòng đọc thông báo và đồng ý trước khi chụp giấy tờ.");
        }
        EkycPurpose purpose = consent.Purpose;
        await RequireNotVerifiedAsync(userId, purpose, ct);

        byte[] image = await ReadImageAsync(content, contentType, ct);

        // Đọc trước, hủy lượt cũ sau: ảnh lỗi thì lượt Pending đang có vẫn giữ nguyên.
        EkycDocumentReading reading = await ActiveClient().ReadFrontAsync(image, fileName, contentType, ct);

        // Owner đăng ký phải đọc được đủ CCCD, họ tên, ngày sinh, và đã gắn danh tính thì phải ra đúng danh tính đó.
        OwnerIdentityData? enrollmentIdentity = null;
        if (purpose == EkycPurpose.OwnerEnrollment && reading.DocumentToken != null)
        {
            enrollmentIdentity = await CheckEnrollmentIdentityAsync(userId, reading, ct);
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
            ResidenceAddress = reading.ResidenceAddress,
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
        verification.CitizenId = EkycRules.GetCitizenId(reading.IdNumber);
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
        if (verification.Id != request.VerificationId || verification.Purpose != purpose)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                "Lượt xác minh đã thay đổi. Vui lòng đọc lại mặt trước giấy tờ.");
        }

        // Bản tự khai không nâng kết quả từ ảnh. Owner đăng ký: họ tên, ngày sinh và nơi cư trú Owner xác nhận được lưu
        // MÃ HÓA vào bản tạm danh tính để gắn vào OwnerIdentity khi lượt đạt, vì OCR hay đọc sai tên tiếng Việt và tên sai
        // bị lưu thì lần eKYC sau không đối chiếu được. Số CCCD vẫn giữ theo OCR: kiểm tra trùng CCCD giữa các tài khoản
        // và với người nhận đã chạy trên số đọc từ ảnh. Đã có danh tính gắn trước đó thì bản tạm không được dùng.
        if (purpose == EkycPurpose.OwnerEnrollment)
        {
            OwnerIdentityData? pending = identityProtector.UnprotectPending(verification);
            if (pending != null)
            {
                // Họ tên/ngày sinh khai lệch xa so với OCR: từ chối, không lưu giá trị khai. Nhật ký chỉ ghi mã lệch,
                // không ghi tên hay ngày sinh (SEC-07). Chưa có người duyệt tay nên Owner tự sửa lại hoặc chụp lại.
                string? mismatch = EkycRules.FindDeclaredMismatch(
                    pending, request.FullName, request.DateOfBirth!.Value, ekycOptions.NameSimilarityThreshold);
                if (mismatch != null)
                {
                    auditLogRepository?.Add(AuditRules.Create(
                        null, userId, AuditActorRole.Owner, AuditAction.EkycFailed, AuditTargetType.User, userId, null,
                        AuditOutcome.Failed, mismatch, timeProvider.GetUtcNow()));
                    await unitOfWork.SaveChangesAsync(ct);
                    throw ValidationErrors.For(
                        mismatch == "DOB_MISMATCH" ? "dateOfBirth" : "fullName",
                        "Thông tin khai không khớp giấy tờ đã đọc. Hãy kiểm tra lại hoặc chụp lại mặt trước rõ hơn.");
                }
                pending.FullName = EkycRules.NormalizeName(request.FullName);
                pending.DateOfBirth = request.DateOfBirth!.Value;
                pending.ResidenceAddress = request.ResidenceAddress.Trim();
                identityProtector.ProtectPending(verification, pending);
                await unitOfWork.SaveChangesAsync(ct);
            }
        }

        return verification.ToResponse(ekycOptions.CurrentThreshold);
    }

    public async Task<EkycBackResponse> SubmitBackAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        // mặt sau thuộc cùng lần chụp với mặt trước, nên phải có lượt đang chờ selfie.
        EkycVerification? verification = await verificationRepository.FindLatestByUserIdAsync(userId, ct);
        if (verification == null || !EkycRules.CanMatchFace(verification, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                "Vui lòng đọc mặt trước giấy tờ trước khi gửi mặt sau.");
        }

        byte[] image = await ReadImageAsync(content, contentType, ct);
        EkycBackReading reading = await ActiveClient().ReadBackAsync(image, fileName, contentType, ct);

        if (reading.IdNumber.Length > 0 && !EkycRules.IsSameCard(reading.IdNumber, EkycRules.GetCitizenIdLast4(verification.CitizenId)))
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.EkycDocumentMismatch,
                "Hai mặt giấy tờ không cùng một thẻ. Vui lòng chụp lại.");
        }

        // Thẻ không có MRZ chứa số căn cước thì không kết luận được: giữ false, không coi là lỗi.
        verification.BackSideChecked = reading.IdNumber.Length > 0;
        verification.BackSubmittedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        // nơi cư trú đọc từ mặt sau chỉ trả cho người dùng đối chiếu; chỉ bản người dùng xác nhận ở ConfirmDocumentAsync mới được lưu.
        return verification.ToBackResponse(reading.ResidenceAddress, ekycOptions.CurrentThreshold);
    }

    public async Task<EkycLivenessResponse> StartLivenessAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        EkycVerification verification = await RequirePendingVerificationAsync(userId, ct);
        RequireBackSide(verification);

        // Gọi lại thì hủy phiên cũ, lỗi hủy bỏ qua vì dịch vụ tự dọn phiên hết hạn.
        string? previousSession = verification.LivenessSessionId;
        EkycLivenessState state = await ActiveClient().StartLivenessAsync(ct);
        verification.LivenessSessionId = state.SessionId;
        // Phiên mới phải tự đạt lại: không thừa hưởng kết quả đạt của phiên cũ.
        verification.LivenessPassed = false;
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
        EkycLivenessState state = await ActiveClient().SendLivenessFrameAsync(sessionId, frame, fileName, contentType, ct);

        // Ghi nhớ lúc phiên đạt cả động tác và chống giả mạo để đường ảnh selfie tĩnh cũng dùng được.
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

        // Backend tự đòi phiên đã được dịch vụ báo đạt (cả động tác lẫn chống giả mạo) ở bước gửi khung hình,
        // không tin việc dịch vụ trả được ảnh.
        if (!verification.LivenessPassed)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EkycLivenessNotPassed,
                "Bạn chưa hoàn thành thử thách camera.");
        }

        // selfie lấy từ chính phiên đã đạt. Dịch vụ kiểm lại lúc trả ảnh (phiên đổi mặt hoặc hỏng sau khi đạt thì
        // từ chối, EKYC_LIVENESS_NOT_PASSED).
        byte[] selfie = await ActiveClient().GetLivenessSelfieAsync(sessionId, ct);

        verification.SpoofCheckPassed = true;
        double threshold = ekycOptions.CurrentThreshold;

        EkycFaceComparison comparison = await ActiveClient().CompareFaceAsync(
            documentToken, selfie, "liveness.jpg", "image/jpeg", threshold, ct);
        verification.FaceMatchScore = comparison.Score;
        verification.LivenessSessionId = null;

        // Tự so lại với ngưỡng của server, không tin cờ matched do dịch vụ ngoài trả về.
        if (comparison.Score >= threshold)
        {
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
    /// Owner đã có lượt OWNER_ENROLLMENT mới nhất đạt thì khóa, không cho xác minh lại. Chưa đạt (chưa làm, đang chờ,
    /// thất bại) thì cho tiếp tục.
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
            await ActiveClient().DeleteLivenessAsync(sessionId, ct);
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
    /// Dịch vụ eKYC đang dùng: bình thường là dịch vụ Python; khi Admin "tạm ngừng eKYC" (công cụ thử nghiệm) thì là bản giả,
    /// luôn đạt, đọc giấy tờ ra một danh tính cố định theo tài khoản.
    /// </summary>
    private IEkycClient ActiveClient()
    {
        if (!ekycOptions.Paused)
        {
            return ekycClient;
        }
        return new SimulatedEkycClient(currentUser.RequireUserId());
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

    /// <summary>
    /// Đọc danh tính từ mặt trước của Owner đăng ký và đối chiếu với danh tính đã gắn nếu có.
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
            // Lần gắn danh tính đầu tiên: CCCD của Chủ tài sản không được trùng người nhận đã thêm trước đó.
            await EnsureNotAmongBeneficiariesAsync(userId, readIdentity.CitizenId, reading.DocumentToken!, ct);
            // CCCD của Chủ tài sản là duy nhất trên toàn hệ thống, không được trùng với tài khoản khác đã gắn danh tính.
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
    /// Số CCCD của Chủ tài sản phải là duy nhất trên toàn hệ thống (mỗi công dân chỉ gắn với một tài khoản).
    /// </summary>
    private async Task EnsureCitizenIdNotUsedByAnotherUserAsync(
        Guid userId, string citizenId, string? documentToken, CancellationToken ct)
    {
        // Dòng đã có hash: tra bằng index. Dòng cũ chưa backfill vẫn dò bằng giải mã bên dưới.
        string? hash = citizenIdIndex?.Compute(citizenId);
        if (hash != null && await identityRepository.ExistsByCitizenIdHashExceptUserAsync(hash, userId, ct))
        {
            throw await CitizenIdAlreadyUsedAsync(documentToken, ct);
        }

        string last4 = citizenId.Substring(citizenId.Length - 4);
        List<OwnerIdentity> candidates = await identityRepository.ListByCitizenIdLast4ExceptUserAsync(last4, userId, ct);
        foreach (OwnerIdentity candidate in candidates)
        {
            OwnerIdentityData data = identityProtector.Unprotect(candidate);
            if (string.Equals(data.CitizenId, citizenId, StringComparison.Ordinal))
            {
                throw await CitizenIdAlreadyUsedAsync(documentToken, ct);
            }
        }
    }

    private async Task<AppException> CitizenIdAlreadyUsedAsync(string? documentToken, CancellationToken ct)
    {
        if (documentToken != null)
        {
            await TryDeleteDocumentAsync(documentToken, ct);
        }
        return new AppException(AppErrorType.Conflict, ErrorCode.EkycIdentityMismatch,
            "Số CCCD này đã được sử dụng để xác minh cho một tài khoản khác trong hệ thống.",
            new Dictionary<string, object?> { ["reason"] = "CITIZEN_ID_ALREADY_USED" });
    }

    /// <summary>
    /// Chủ tài sản chưa gắn danh tính nên lúc thêm người nhận chưa so được CCCD. Đối chiếu ngược khi gắn: nếu CCCD trên giấy tờ
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
                    "Số CCCD trên giấy tờ trùng với một người nhận bạn đã thêm. Người nhận không được trùng với Chủ tài sản; hãy xóa hoặc sửa hồ sơ người nhận đó trước.",
                    new Dictionary<string, object?> { ["reason"] = "BENEFICIARY_CONFLICT" });
            }
        }
    }

    /// <summary>
    /// Gắn danh tính cho Owner khi lượt OWNER_ENROLLMENT đạt. Đã có danh tính thì giữ nguyên (đã đối chiếu
    /// lúc đọc giấy tờ). Chỉ đánh dấu INSERT; lưu cùng lần FinishAsync.
    /// </summary>
    private async Task BindIdentityAsync(EkycVerification verification, DateTimeOffset now, CancellationToken ct)
    {
        OwnerIdentity? existing = await identityRepository.FindByUserIdAsync(verification.UserId, ct);
        if (existing != null)
        {
            // Danh tính gắn trước khi có ảnh (trường hợp cũ): lượt đạt mới bổ sung ảnh, không thay ảnh đã có.
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
    /// Lưu MÃ HÓA ảnh mặt trước CCCD tạm trên lượt OWNER_ENROLLMENT. Ảnh không phải JPEG/PNG/WebP theo nội
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
        // Chỉ ghi kết quả, không ghi điểm số, họ tên hay số giấy tờ (SEC-07).
        auditLogRepository?.Add(AuditRules.Create(
            null, verification.UserId, AuditActorRole.Owner,
            status == EkycStatus.Passed ? AuditAction.EkycPassed : AuditAction.EkycFailed,
            AuditTargetType.User, verification.UserId, null,
            status == EkycStatus.Passed ? AuditOutcome.Succeeded : AuditOutcome.Failed,
            verification.Purpose.ToApiString(), now));
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (identityRepository.IsCitizenIdHashViolation(exception))
        {
            // Hai yêu cầu cùng lúc gắn cùng một CCCD: DB chặn yêu cầu đến sau, trả lỗi nghiệp vụ thay vì 500.
            await IdentityImageStorage.TryDeleteAsync(contentStore, discardedImageKey);
            throw await CitizenIdAlreadyUsedAsync(documentToken, ct);
        }
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
            await ActiveClient().DeleteDocumentAsync(documentToken, ct);
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
