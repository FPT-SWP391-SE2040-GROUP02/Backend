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

// Người nhận nộp hồ sơ ở bước xác minh danh tính, theo thứ tự:
//   1. Nộp hồ sơ (Chấp nhận hoặc Từ chối, thông tin khai, ảnh căn cước hai mặt) ... SubmitAsync
//   2. Xem lại hồ sơ mới nhất đã nộp ................................................. GetLatestAsync
// Hồ sơ này là bước 2 của biên bản bàn giao: luật sư đối chiếu rồi xác nhận danh tính người nhận.
/// <summary>Người nhận nộp hồ sơ: đối chiếu với dữ liệu Chủ kho đã khai, lưu ảnh mã hóa, ghi chữ ký vào biên bản và báo Người xét duyệt.</summary>
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
    private readonly IRecipientDeclaredDataProtector? declaredProtector;

    public HandoverSubmissionService(
        ICurrentUser currentUser,
        IHandoverRepository handover,
        IHandoverSubmissionRepository submissions,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IRecipientDeclaredDataProtector? declaredProtector = null)
    {
        this.declaredProtector = declaredProtector;
        this.currentUser = currentUser;
        this.handover = handover;
        this.submissions = submissions;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.contentStore = contentStore;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    // Nút "Nộp hồ sơ" ở cổng người nhận.
    public async Task<HandoverSubmissionResponse> SubmitAsync(
        Guid turnId, SubmitHandoverSubmissionRequest request, byte[] frontImage, byte[] backImage, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);

        // 2. Lượt phải đang ở bước nộp hồ sơ người nhận
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không ở bước nộp hồ sơ người nhận.");
        }

        // 3. Phải tick cam đoan thông tin khai là đúng. Hồ sơ này chỉ để luật sư kiểm danh tính;
        //    việc nhận tài sản theo biên bản bàn giao, người nhận ký ở bước sau.
        if (!request.Acknowledged)
        {
            throw ValidationErrors.For("acknowledged", "Vui lòng cam đoan thông tin đã khai là đúng.");
        }

        // 4. Hai ảnh căn cước phải là JPEG, PNG hoặc WEBP
        string? frontType = IdentityImageStorage.DetectContentType(frontImage);
        string? backType = IdentityImageStorage.DetectContentType(backImage);
        if (frontType == null || backType == null)
        {
            throw ValidationErrors.For("images", "Cần ảnh căn cước hai mặt dạng JPEG, PNG hoặc WEBP.");
        }

        // 5. Đọc thông tin Chủ kho đã khai cho người nhận để đối chiếu; hồ sơ đã được duyệt thì không nộp lại
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary == null)
        {
            throw HandoverAccess.NotFound();
        }
        BeneficiaryPersonalData expected = protector.Unprotect(beneficiary);

        HandoverSubmission? latest = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        if (latest != null && latest.Status == HandoverSubmissionStatus.Approved)
        {
            throw HandoverAccess.NotAllowed("ALREADY_APPROVED", "Hồ sơ của bạn đã được duyệt.");
        }

        // 6. Tạo hồ sơ mới: giữ số căn cước khai và ba kết quả đối chiếu tên, số căn cước, ngày sinh
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverSubmission submission = new HandoverSubmission
        {
            TurnId = turn.Id,
            Kind = HandoverSubmissionKind.Accept,
            Status = HandoverSubmissionStatus.Submitted
        };
        submission.NameMatches = EkycRules.NormalizeNameForCompare(request.FullName)
            == EkycRules.NormalizeNameForCompare(expected.FullName);
        submission.CitizenIdMatches = string.Equals(request.CitizenId, expected.CitizenId, StringComparison.Ordinal);
        submission.DateOfBirthMatches = request.DateOfBirth == expected.DateOfBirth;
        submission.CitizenId = request.CitizenId;
        submission.Acknowledged = true;
        submission.VoluntaryDecline = false;
        submission.CreatedAt = now;

        // 7. Lưu mã hóa hai ảnh, đánh dấu bản cũ bị thay, rồi lưu một lần.
        //    Lỗi giữa chừng thì xóa các ảnh vừa lưu.
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

        // 8. Sau khi lưu: xóa ảnh của bản cũ, trả kết quả.
        // Bản cũ bị thay thế: xóa ảnh của nó, không giữ ảnh căn cước lâu hơn cần thiết.
        if (latest != null)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, latest.FrontImageStorageKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, latest.BackImageStorageKey);
        }
        return submission.ToResponse();
    }

    // Màn hồ sơ của người nhận: xem hồ sơ mới nhất đã nộp.
    public async Task<HandoverSubmissionResponse?> GetLatestAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);
        // 2. Trả hồ sơ mới nhất, chưa nộp thì null
        HandoverSubmission? latest = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        if (latest == null)
        {
            return null;
        }
        return latest.ToResponse();
    }
}
