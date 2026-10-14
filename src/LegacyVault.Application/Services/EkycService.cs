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
    private readonly IEkycClient ekycClient;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public EkycService(
        ICurrentUser currentUser,
        IEkycVerificationRepository verificationRepository,
        IEkycClient ekycClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.verificationRepository = verificationRepository;
        this.ekycClient = ekycClient;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<EkycDocumentResponse> SubmitDocumentAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        byte[] image = await ReadImageAsync(content, contentType, ct);

        // Đọc trước, hủy lượt cũ sau: ảnh lỗi thì lượt Pending đang có vẫn giữ nguyên.
        EkycDocumentReading reading = await ekycClient.ReadFrontAsync(image, fileName, contentType, ct);

        DateTimeOffset now = timeProvider.GetUtcNow();
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
