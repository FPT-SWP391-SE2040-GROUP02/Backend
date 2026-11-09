using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IHandoverSubmissionService
{
    /// <summary>
    /// Người nhận nộp hồ sơ (Chấp nhận hoặc Từ chối, thông tin khai trên văn bản mẫu và ảnh căn cước hai mặt) cho Người xét duyệt.
    /// Chỉ khi lượt đang ở bước xác minh danh tính. Nộp lại thì bản cũ bị thay thế.
    /// </summary>
    Task<HandoverSubmissionResponse> SubmitAsync(
        Guid turnId, SubmitHandoverSubmissionRequest request, byte[] frontImage, byte[] backImage, CancellationToken ct);

    /// <summary>Hồ sơ mới nhất của lượt này; null khi chưa nộp.</summary>
    Task<HandoverSubmissionResponse?> GetLatestAsync(Guid turnId, CancellationToken ct);
}

public sealed class HandoverSubmissionService : IHandoverSubmissionService
{
    private readonly ICurrentUser currentUser;
    private readonly IHandoverRepository handover;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IAssetContentStore contentStore;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverSubmissionService(
        ICurrentUser currentUser,
        IHandoverRepository handover,
        IHandoverSubmissionRepository submissions,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.handover = handover;
        this.submissions = submissions;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.contentStore = contentStore;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<HandoverSubmissionResponse> SubmitAsync(
        Guid turnId, SubmitHandoverSubmissionRequest request, byte[] frontImage, byte[] backImage, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);

        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước nộp hồ sơ người nhận.");
        }

        HandoverSubmissionKind kind = request.Kind == "DECLINE" ? HandoverSubmissionKind.Decline : HandoverSubmissionKind.Accept;
        if (!request.Acknowledged)
        {
            throw ValidationErrors.For("acknowledged", "Vui lòng xác nhận đã đọc điều khoản của lựa chọn này.");
        }
        if (kind == HandoverSubmissionKind.Decline && !request.VoluntaryDecline)
        {
            throw ValidationErrors.For("voluntaryDecline", "Vui lòng xác nhận tự nguyện từ chối, không nhằm trốn tránh nghĩa vụ tài sản.");
        }

        string? frontType = IdentityImageStorage.DetectContentType(frontImage);
        string? backType = IdentityImageStorage.DetectContentType(backImage);
        if (frontType == null || backType == null)
        {
            throw ValidationErrors.For("images", "Cần ảnh căn cước hai mặt dạng JPEG, PNG hoặc WEBP.");
        }

        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary == null)
        {
            throw HandoverNotFound();
        }
        BeneficiaryPersonalData expected = protector.Unprotect(beneficiary);

        HandoverSubmission? latest = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        if (latest != null && latest.Status == HandoverSubmissionStatus.Approved)
        {
            throw NotAllowed("ALREADY_APPROVED", "Hồ sơ của bạn đã được duyệt.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverSubmission submission = new HandoverSubmission();
        submission.TurnId = turn.Id;
        submission.Kind = kind;
        submission.Status = HandoverSubmissionStatus.Submitted;
        submission.NameMatches = EkycRules.NormalizeNameForCompare(request.FullName)
            == EkycRules.NormalizeNameForCompare(expected.FullName);
        submission.CitizenIdMatches = string.Equals(request.CitizenId, expected.CitizenId, StringComparison.Ordinal);
        submission.DateOfBirthMatches = request.DateOfBirth == expected.DateOfBirth;
        submission.CitizenIdLast4 = request.CitizenId.Substring(request.CitizenId.Length - 4);
        submission.Acknowledged = true;
        submission.VoluntaryDecline = request.VoluntaryDecline;
        submission.CreatedAt = now;

        string? frontKey = null;
        string? backKey = null;
        try
        {
            StoredContent front = await IdentityImageStorage.SaveAsync(contentStore, frontImage, ct);
            frontKey = front.StorageKey;
            StoredContent back = await IdentityImageStorage.SaveAsync(contentStore, backImage, ct);
            backKey = back.StorageKey;

            submission.FrontImageStorageKey = front.StorageKey;
            submission.FrontImageContentType = frontType;
            submission.FrontImageSizeBytes = front.SizeBytes;
            submission.FrontImageEncryptionKeyId = front.EncryptionKeyId;
            submission.FrontImageWrappedDataKey = front.WrappedDataKey;
            submission.BackImageStorageKey = back.StorageKey;
            submission.BackImageContentType = backType;
            submission.BackImageSizeBytes = back.SizeBytes;
            submission.BackImageEncryptionKeyId = back.EncryptionKeyId;
            submission.BackImageWrappedDataKey = back.WrappedDataKey;

            if (latest != null)
            {
                latest.Status = HandoverSubmissionStatus.Superseded;
                latest.UpdatedAt = now;
            }
            submissions.Add(submission);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Bù trừ: không để lại ảnh không có hồ sơ. Không dùng ct vì request có thể đã bị hủy.
            await IdentityImageStorage.TryDeleteAsync(contentStore, frontKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, backKey);
            throw;
        }

        // Bản cũ bị thay thế: xóa ảnh của nó, không giữ ảnh căn cước lâu hơn cần thiết.
        if (latest != null)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, latest.FrontImageStorageKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, latest.BackImageStorageKey);
        }
        return ToResponse(submission);
    }

    public async Task<HandoverSubmissionResponse?> GetLatestAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);
        HandoverSubmission? latest = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        return latest == null ? null : ToResponse(latest);
    }

    internal static HandoverSubmissionResponse ToResponse(HandoverSubmission submission)
    {
        HandoverSubmissionResponse response = new HandoverSubmissionResponse();
        response.Id = submission.Id;
        response.TurnId = submission.TurnId;
        response.Kind = submission.Kind.ToApiString();
        response.Status = submission.Status.ToApiString();
        response.SupplementReason = submission.SupplementReason;
        response.CreatedAt = submission.CreatedAt;
        return response;
    }

    /// <summary>Lượt của người đang đăng nhập; id lạ và lượt của người khác cho cùng một lỗi (SEC-04).</summary>
    private async Task<HandoverTurn> FindMyTurnAsync(Guid turnId, Guid userId, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.ClaimantUserId != userId)
        {
            throw HandoverNotFound();
        }
        return turn;
    }

    private static AppException HandoverNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy nội dung bàn giao.");
    }

    private static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
