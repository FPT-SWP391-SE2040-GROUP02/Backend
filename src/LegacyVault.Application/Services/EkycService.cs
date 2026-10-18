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
        verificationRepository.Add(verification);
        await unitOfWork.SaveChangesAsync(ct);

        response.VerificationId = verification.Id;
        response.CanMatchFace = true;
        response.ExpiresAt = verification.DocumentTokenExpiresAt;
        return response;
    }

    public async Task<EkycStatusResponse> SubmitBackAsync(
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
        return verification.ToResponse();
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
            await FinishAsync(verification, EkycStatus.Passed, now, ct);
        }
        else
        {
            await FinishAsync(verification, EkycStatus.Failed, now, ct);
        }
        await TryDeleteLivenessAsync(sessionId, ct);
        return verification.ToResponse();
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

    /// <summary>Chốt kết quả, xóa mã tham chiếu ở cả DB và dịch vụ eKYC để mỗi lượt chỉ so khớp một lần.</summary>
    private async Task FinishAsync(EkycVerification verification, EkycStatus status, DateTimeOffset now, CancellationToken ct)
    {
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
