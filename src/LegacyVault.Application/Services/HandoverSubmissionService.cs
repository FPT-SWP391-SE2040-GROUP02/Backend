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

// Người nhận nộp hồ sơ ở bước xác minh danh tính, theo thứ tự:
//   1. Nộp hồ sơ (Chấp nhận hoặc Từ chối, thông tin khai, ảnh căn cước hai mặt) ... SubmitAsync
//   2. Xem lại hồ sơ mới nhất đã nộp ................................................. GetLatestAsync
// Lượt có biên bản: hồ sơ này là phần ký của người nhận, ảnh văn bản dựng lúc Người xét duyệt gửi.
// Lượt không có biên bản (luồng cũ): ảnh văn bản dựng ngay lúc nộp (StorePaperAsync).
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
    private readonly IHandoverPaperWriter? paperWriter;
    private readonly IHandoverDocumentRepository? documents;
    private readonly IHandoverDocumentNotifier? notifier;
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
        IHandoverPaperWriter? paperWriter = null,
        IHandoverDocumentRepository? documents = null,
        IHandoverDocumentNotifier? notifier = null,
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
        this.paperWriter = paperWriter;
        this.documents = documents;
        this.notifier = notifier;
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

        // 3. Đọc lựa chọn; phải tick đã đọc điều khoản, và tick tự nguyện nếu từ chối
        HandoverSubmissionKind kind = HandoverDocumentRules.ParseKind(request.Kind);
        if (!request.Acknowledged)
        {
            throw ValidationErrors.For("acknowledged", "Vui lòng xác nhận đã đọc điều khoản của lựa chọn này.");
        }
        if (kind == HandoverSubmissionKind.Decline && !request.VoluntaryDecline)
        {
            throw ValidationErrors.For("voluntaryDecline", "Vui lòng xác nhận tự nguyện từ chối, không nhằm trốn tránh nghĩa vụ tài sản.");
        }

        // 4. Hai ảnh căn cước phải là JPEG, PNG hoặc WEBP
        string? frontType = IdentityImageStorage.DetectContentType(frontImage);
        string? backType = IdentityImageStorage.DetectContentType(backImage);
        if (frontType == null || backType == null)
        {
            throw ValidationErrors.For("images", "Cần ảnh căn cước hai mặt dạng JPEG, PNG hoặc WEBP.");
        }

        // 5. Nếu lượt có biên bản: biên bản phải đang chờ người nhận và lựa chọn phải trùng biên bản.
        // Lượt đã có biên bản thì hồ sơ này là phần ký của người nhận: chỉ nộp được khi biên bản đang chờ người nhận (Phần 1 đã chép sẵn từ hồ sơ chứng tử).
        HandoverDocument? document = null;
        if (documents != null)
        {
            document = await documents.FindByTurnIdAsync(turn.Id, ct);
        }
        if (document != null)
        {
            if (document.Status != HandoverDocumentStatus.WaitRecipient)
            {
                throw HandoverAccess.NotAllowed("DOCUMENT_NOT_READY", "Biên bản chưa đến lượt bạn điền và ký.");
            }
            if (document.Kind != kind)
            {
                throw ValidationErrors.For("kind", "Lựa chọn phải trùng với biên bản đã bắt đầu.");
            }
        }

        // 6. Đọc thông tin Chủ kho đã khai cho người nhận để đối chiếu; hồ sơ đã được duyệt thì không nộp lại
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

        // 7. Tạo hồ sơ mới: chỉ giữ 4 số cuối căn cước và ba kết quả đối chiếu tên, số căn cước, ngày sinh
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverSubmission submission = new HandoverSubmission
        {
            TurnId = turn.Id,
            Kind = kind,
            Status = HandoverSubmissionStatus.Submitted
        };
        submission.NameMatches = EkycRules.NormalizeNameForCompare(request.FullName)
            == EkycRules.NormalizeNameForCompare(expected.FullName);
        submission.CitizenIdMatches = string.Equals(request.CitizenId, expected.CitizenId, StringComparison.Ordinal);
        submission.DateOfBirthMatches = request.DateOfBirth == expected.DateOfBirth;
        submission.CitizenIdLast4 = request.CitizenId.Substring(request.CitizenId.Length - 4);
        submission.Acknowledged = true;
        submission.VoluntaryDecline = request.VoluntaryDecline;
        submission.CreatedAt = now;

        // 8. Lưu mã hóa hai ảnh, cập nhật biên bản nếu có, đánh dấu bản cũ bị thay, rồi lưu một lần.
        //    Lỗi giữa chừng thì xóa các ảnh vừa lưu.
        string? frontKey = null;
        string? backKey = null;
        string? paperKey = null;
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

            // Có biên bản thì ảnh văn bản được dựng lúc Người xét duyệt gửi, khi người nhận đã ký và Người xét duyệt đã kiểm tra.
            if (document == null)
            {
                paperKey = await StorePaperAsync(submission, turn, ct);
            }

            if (latest != null)
            {
                latest.Status = HandoverSubmissionStatus.Superseded;
                latest.UpdatedAt = now;
            }
            submissions.Add(submission);
            if (document != null)
            {
                document.RecipientFullName = request.FullName.Trim();
                document.RecipientBirthYear = request.DateOfBirth!.Value.Year;
                document.RecipientCitizenIdLast4 = submission.CitizenIdLast4;
                if (declaredProtector != null)
                {
                    RecipientDeclaredData declaredData = new RecipientDeclaredData
                    {
                        CitizenId = request.CitizenId,
                        DateOfBirth = request.DateOfBirth
                    };
                    document.RecipientDeclaredProtected = declaredProtector.Protect(declaredData);
                }
                document.RecipientResidenceAddress = HandoverDocumentRules.CleanAddress(request.ResidenceAddress);
                document.RecipientSignedAt = now;
                document.RecipientTermsVersion = HandoverDocumentRules.TermsVersion;
                document.SubmissionId = submission.Id;
                document.SupplementReason = null;
                document.Status = HandoverDocumentStatus.WaitVerifier;
                document.UpdatedAt = now;
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Bù trừ: không để lại ảnh không có hồ sơ. Không dùng ct vì request có thể đã bị hủy.
            await IdentityImageStorage.TryDeleteAsync(contentStore, frontKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, backKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, paperKey);
            throw;
        }

        // 9. Sau khi lưu: xóa ảnh của bản cũ, báo Người xét duyệt nếu có biên bản, trả kết quả.
        // Bản cũ bị thay thế: xóa ảnh của nó, không giữ ảnh căn cước lâu hơn cần thiết.
        if (latest != null)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, latest.FrontImageStorageKey);
            await IdentityImageStorage.TryDeleteAsync(contentStore, latest.BackImageStorageKey);
        }
        if (document != null && notifier != null)
        {
            await notifier.VerifierTurnAsync(document, ct);
        }
        return submission.ToResponse();
    }

    /// <summary>
    /// Dựng và lưu mã hóa ảnh văn bản ngay lúc nộp, để có bản làm bằng chứng kể cả khi hồ sơ bị thay thế hoặc chưa được duyệt.
    /// Dựng lỗi thì không chặn người nhận nộp hồ sơ: trả null và bước duyệt sẽ dựng bù.
    /// </summary>
    private async Task<string?> StorePaperAsync(HandoverSubmission submission, HandoverTurn turn, CancellationToken ct)
    {
        if (paperWriter == null)
        {
            return null;
        }
        try
        {
            HandoverPaperRequest paperRequest = new HandoverPaperRequest { Submission = submission, Turn = turn };
            return await paperWriter.WriteAsync(paperRequest, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
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
