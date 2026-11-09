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

public interface IHandoverSubmissionReviewService
{
    /// <summary>Hồ sơ người nhận đang chờ tôi duyệt (chỉ kho mà tôi đã duyệt hồ sơ chứng tử).</summary>
    Task<List<VerifierHandoverSubmissionSummaryResponse>> ListPendingAsync(CancellationToken ct);

    Task<VerifierHandoverSubmissionResponse> GetAsync(Guid submissionId, CancellationToken ct);

    /// <summary>Ảnh căn cước một mặt ("front" hoặc "back"), giải mã khi đọc, có kiểm toàn vẹn.</summary>
    Task<PreparedDownload> PrepareImageAsync(Guid submissionId, string side, CancellationToken ct);

    /// <summary>Duyệt: chỉ khi cả ba trường khớp. Lượt sang bước quyết định (READY_TO_ACCEPT).</summary>
    Task<VerifierHandoverSubmissionResponse> ApproveAsync(Guid submissionId, CancellationToken ct);

    /// <summary>Yêu cầu người nhận bổ sung kèm lý do; người nhận nộp lại bản mới.</summary>
    Task<VerifierHandoverSubmissionResponse> RequestSupplementAsync(
        Guid submissionId, RequestHandoverSupplementRequest request, CancellationToken ct);
}

public sealed class HandoverSubmissionReviewService : IHandoverSubmissionReviewService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IHoldReader holdReader;
    private readonly IAssetContentStore contentStore;
    private readonly IAuditLogRepository auditLogs;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IBeneficiaryRepository? beneficiaries;
    private readonly IBeneficiaryDataProtector? protector;
    private readonly IDeclinePaperRenderer? paperRenderer;

    public HandoverSubmissionReviewService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IHandoverSubmissionRepository submissions,
        IHoldReader holdReader,
        IAssetContentStore contentStore,
        IAuditLogRepository auditLogs,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IBeneficiaryRepository? beneficiaries = null,
        IBeneficiaryDataProtector? protector = null,
        IDeclinePaperRenderer? paperRenderer = null)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.submissions = submissions;
        this.holdReader = holdReader;
        this.contentStore = contentStore;
        this.auditLogs = auditLogs;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.paperRenderer = paperRenderer;
    }

    public async Task<List<VerifierHandoverSubmissionSummaryResponse>> ListPendingAsync(CancellationToken ct)
    {
        Guid userId = await RequireActiveVerifierAsync(ct);
        List<HandoverSubmission> pending = await submissions.ListPendingForVerifierAsync(userId, ct);
        List<VerifierHandoverSubmissionSummaryResponse> list = new List<VerifierHandoverSubmissionSummaryResponse>();
        foreach (HandoverSubmission submission in pending)
        {
            VerifierHandoverSubmissionSummaryResponse row = new VerifierHandoverSubmissionSummaryResponse();
            FillSummary(row, submission);
            list.Add(row);
        }
        return list;
    }

    public async Task<VerifierHandoverSubmissionResponse> GetAsync(Guid submissionId, CancellationToken ct)
    {
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        return ToDetail(submission);
    }

    public async Task<PreparedDownload> PrepareImageAsync(Guid submissionId, string side, CancellationToken ct)
    {
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        bool front = string.Equals(side, "front", StringComparison.OrdinalIgnoreCase);
        if (!front && !string.Equals(side, "back", StringComparison.OrdinalIgnoreCase))
        {
            throw SubmissionNotFound();
        }

        // Hồ sơ đã bị thay thế thì ảnh đã xóa: không còn gì để xem.
        if (submission.Status == HandoverSubmissionStatus.Superseded)
        {
            throw SubmissionNotFound();
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = front ? submission.FrontImageStorageKey : submission.BackImageStorageKey;
        stored.SizeBytes = front ? submission.FrontImageSizeBytes : submission.BackImageSizeBytes;
        stored.EncryptionKeyId = front ? submission.FrontImageEncryptionKeyId : submission.BackImageEncryptionKeyId;
        stored.WrappedDataKey = front ? submission.FrontImageWrappedDataKey : submission.BackImageWrappedDataKey;
        string contentType = front ? submission.FrontImageContentType : submission.BackImageContentType;
        return await IdentityImageStorage.PrepareAsync(
            contentStore, stored, contentType, front ? "cccd-mat-truoc" : "cccd-mat-sau", ct);
    }

    public async Task<VerifierHandoverSubmissionResponse> ApproveAsync(Guid submissionId, CancellationToken ct)
    {
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = submission.Turn;

        // Bấm lặp: đã duyệt rồi thì trả lại kết quả cũ.
        if (submission.Status == HandoverSubmissionStatus.Approved)
        {
            return ToDetail(submission);
        }
        if (submission.Status != HandoverSubmissionStatus.Submitted)
        {
            throw NotAllowed("WRONG_STATE", "Hồ sơ này không còn ở trạng thái chờ duyệt.");
        }
        if (!(submission.NameMatches && submission.CitizenIdMatches && submission.DateOfBirthMatches))
        {
            throw NotAllowed("FIELDS_MISMATCH", "Có trường không khớp thông tin Chủ kho đã khai. Chỉ yêu cầu bổ sung được.");
        }
        Vault vault = await RequireVaultInHandoverAsync(turn, ct);
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        submission.Status = HandoverSubmissionStatus.Approved;
        submission.ReviewedByUserId = userId;
        submission.ReviewedAt = now;
        submission.UpdatedAt = now;

        HandoverRules.MarkReadyToAccept(turn, now);
        turn.IdentityConfirmedByUserId = userId;
        turn.IdentityConfirmedAt = now;
        // Chạm kế hoạch để RowVersion của nó chặn việc này chạy cùng lúc với đặt hold hay báo còn sống.
        vault.Plan.UpdatedAt = now;

        string? paperKey = null;
        if (submission.Kind == HandoverSubmissionKind.Decline)
        {
            paperKey = await StoreDeclinePaperAsync(submission, vault, userId, now, ct);
        }

        auditLogs.Add(AuditRules.Create(
            turn.VaultId, userId, AuditActorRole.Verifier, AuditAction.HandoverSubmissionApproved,
            AuditTargetType.HandoverTurn, turn.Id, null, AuditOutcome.Succeeded, null, now));
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, paperKey);
            throw;
        }
        return ToDetail(submission);
    }

    /// <summary>Dựng và lưu mã hóa ảnh văn bản từ chối; trả khóa lưu để bù trừ khi lưu bản ghi thất bại. Thiếu bộ dựng thì bỏ qua.</summary>
    private async Task<string?> StoreDeclinePaperAsync(
        HandoverSubmission submission, Vault vault, Guid reviewerId, DateTimeOffset now, CancellationToken ct)
    {
        if (paperRenderer == null || beneficiaries == null || protector == null)
        {
            return null;
        }
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, submission.Turn.BeneficiaryId, ct);
        User? reviewer = await users.FindByIdAsync(reviewerId, ct);
        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        if (beneficiary == null || reviewer == null)
        {
            return null;
        }

        DeclinePaperData data = new DeclinePaperData();
        data.TurnCode = submission.TurnId.ToString("N").Substring(24).ToUpperInvariant();
        data.ContainerName = submission.Turn.ContainerName;
        data.OwnerFullName = owner?.FullName ?? string.Empty;
        data.DeclinerFullName = protector.Unprotect(beneficiary).FullName;
        data.CitizenIdLast4 = submission.CitizenIdLast4;
        data.SubmittedAt = submission.CreatedAt;
        data.NameMatches = submission.NameMatches;
        data.CitizenIdMatches = submission.CitizenIdMatches;
        data.DateOfBirthMatches = submission.DateOfBirthMatches;
        data.ReviewerFullName = reviewer.FullName;
        data.ReviewerLawyerCardNumber = reviewer.LawyerCardNumber;
        data.ReviewedAt = now;

        byte[] image = paperRenderer.Render(data);
        StoredContent stored = await IdentityImageStorage.SaveAsync(contentStore, image, ct);
        submission.PaperStorageKey = stored.StorageKey;
        submission.PaperSizeBytes = stored.SizeBytes;
        submission.PaperEncryptionKeyId = stored.EncryptionKeyId;
        submission.PaperWrappedDataKey = stored.WrappedDataKey;
        return stored.StorageKey;
    }

    public async Task<VerifierHandoverSubmissionResponse> RequestSupplementAsync(
        Guid submissionId, RequestHandoverSupplementRequest request, CancellationToken ct)
    {
        string reason = request.Reason.Trim();
        if (reason.Length == 0)
        {
            throw ValidationErrors.For("reason", "Vui lòng nhập lý do yêu cầu bổ sung.");
        }

        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        Guid userId = currentUser.RequireUserId();
        if (submission.Status != HandoverSubmissionStatus.Submitted)
        {
            throw NotAllowed("WRONG_STATE", "Hồ sơ này không còn ở trạng thái chờ duyệt.");
        }
        if (submission.Turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        submission.Status = HandoverSubmissionStatus.SupplementRequested;
        submission.SupplementReason = reason;
        submission.ReviewedByUserId = userId;
        submission.ReviewedAt = now;
        submission.UpdatedAt = now;

        auditLogs.Add(AuditRules.Create(
            submission.Turn.VaultId, userId, AuditActorRole.Verifier, AuditAction.HandoverSubmissionSupplementRequested,
            AuditTargetType.HandoverTurn, submission.Turn.Id, null, AuditOutcome.Succeeded, null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return ToDetail(submission);
    }

    private async Task<Guid> RequireActiveVerifierAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired,
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }
        if (user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive,
                "Tài khoản đang bị khóa hoặc vô hiệu hóa.");
        }
        if (user.Role != UserRole.Verifier)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.RoleNotAllowed,
                "Chỉ Người xét duyệt mới xét được hồ sơ người nhận.");
        }
        return userId;
    }

    /// <summary>Verifier hợp lệ và là người đã duyệt hồ sơ chứng tử của kho; không thì 404 như hồ sơ không tồn tại (SEC-04).</summary>
    private async Task<HandoverSubmission> LoadForVerifierAsync(Guid submissionId, CancellationToken ct)
    {
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverSubmission? submission = await submissions.FindByIdWithTurnAsync(submissionId, ct);
        if (submission == null || !await submissions.IsApprovingVerifierAsync(userId, submission.Turn.VaultId, ct))
        {
            throw SubmissionNotFound();
        }
        return submission;
    }

    private async Task<Vault> RequireVaultInHandoverAsync(HandoverTurn turn, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw SubmissionNotFound();
        }
        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa duyệt được.");
        }
        return vault;
    }

    private static void FillSummary(VerifierHandoverSubmissionSummaryResponse response, HandoverSubmission submission)
    {
        response.Id = submission.Id;
        response.TurnId = submission.TurnId;
        response.ContainerName = submission.Turn.ContainerName;
        response.Kind = submission.Kind.ToApiString();
        response.AllMatch = submission.NameMatches && submission.CitizenIdMatches && submission.DateOfBirthMatches;
        response.SubmittedAt = submission.CreatedAt;
    }

    private static VerifierHandoverSubmissionResponse ToDetail(HandoverSubmission submission)
    {
        VerifierHandoverSubmissionResponse response = new VerifierHandoverSubmissionResponse();
        FillSummary(response, submission);
        response.Status = submission.Status.ToApiString();
        response.NameMatches = submission.NameMatches;
        response.CitizenIdMatches = submission.CitizenIdMatches;
        response.DateOfBirthMatches = submission.DateOfBirthMatches;
        response.CitizenIdLast4 = submission.CitizenIdLast4;
        response.Acknowledged = submission.Acknowledged;
        response.VoluntaryDecline = submission.VoluntaryDecline;
        response.SupplementReason = submission.SupplementReason;
        response.ItemCount = submission.Turn.Items.Count;
        bool waiting = submission.Status == HandoverSubmissionStatus.Submitted
            && submission.Turn.Status == HandoverTurnStatus.IdentityInProgress;
        response.CanApprove = waiting && response.AllMatch;
        response.CanRequestSupplement = waiting;
        return response;
    }

    private static AppException SubmissionNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy hồ sơ người nhận.");
    }

    private static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
