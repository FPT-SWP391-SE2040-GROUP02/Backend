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

    /// <summary>
    /// Luồng Biên Bản: Người xét duyệt kiểm tra thủ công (tick đủ bốn mục và xác nhận trách nhiệm) rồi gửi.
    /// Hoàn tất biên bản và kết thúc lượt: Chấp nhận thì mở quyền tải tài sản, Từ chối thì ghi nhận từ chối.
    /// Lỗi: 400 chưa tick đủ; 404; 409 WRONG_STATE, NO_DOCUMENT, PLAN_NOT_IN_HANDOVER, HOLD_ACTIVE.
    /// </summary>
    Task<VerifierHandoverSubmissionResponse> CompleteAsync(
        Guid submissionId, ApproveHandoverDocumentRequest request, CancellationToken ct);

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
    private readonly IHandoverPaperWriter? paperWriter;
    private readonly IHandoverRepository? handover;
    private readonly IHandoverDocumentRepository? documents;
    private readonly IHandoverDocumentNotifier? notifier;

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
        IHandoverPaperWriter? paperWriter = null,
        IHandoverRepository? handover = null,
        IHandoverDocumentRepository? documents = null,
        IHandoverDocumentNotifier? notifier = null)
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
        this.paperWriter = paperWriter;
        this.handover = handover;
        this.documents = documents;
        this.notifier = notifier;
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
        if (documents != null && await documents.FindByTurnIdAsync(turn.Id, ct) != null)
        {
            throw NotAllowed("USE_DOCUMENT", "Hồ sơ này thuộc một biên bản. Hãy kiểm tra thủ công rồi bấm gửi.");
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

        // Văn bản đã được dựng lúc người nhận nộp; chỉ dựng bù cho hồ sơ cũ nộp trước khi có bước đó.
        string? paperKey = await StoreMissingPaperAsync(submission, userId, now, ct);

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

    public async Task<VerifierHandoverSubmissionResponse> CompleteAsync(
        Guid submissionId, ApproveHandoverDocumentRequest request, CancellationToken ct)
    {
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = submission.Turn;

        HandoverDocument? document = null;
        if (documents != null)
        {
            document = await documents.FindByTurnIdAsync(turn.Id, ct);
        }
        if (document == null || handover == null)
        {
            throw NotAllowed("NO_DOCUMENT", "Hồ sơ này không có biên bản để hoàn tất.");
        }

        // Bấm lặp: đã hoàn tất rồi thì trả lại kết quả cũ.
        if (document.Status == HandoverDocumentStatus.Completed && submission.Status == HandoverSubmissionStatus.Approved)
        {
            return ToDetail(submission, document);
        }
        if (submission.Status != HandoverSubmissionStatus.Submitted
            || document.Status != HandoverDocumentStatus.WaitVerifier
            || document.SubmissionId != submission.Id)
        {
            throw NotAllowed("WRONG_STATE", "Biên bản này không còn chờ bạn kiểm tra.");
        }
        if (!HandoverDocumentRules.AllChecked(request.Checks))
        {
            throw ValidationErrors.For("checks", "Phải tick đủ cả bốn mục kiểm tra thủ công.");
        }
        if (!request.Acknowledged)
        {
            throw ValidationErrors.For("acknowledged", "Vui lòng tick xác nhận trách nhiệm để gửi.");
        }
        if (turn.Status != HandoverTurnStatus.IdentityInProgress || turn.ClaimantUserId == null)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước lập biên bản.");
        }
        Vault vault = await RequireVaultInHandoverAsync(turn, ct);

        DateTimeOffset now = timeProvider.GetUtcNow();
        submission.Status = HandoverSubmissionStatus.Approved;
        submission.ReviewedByUserId = userId;
        submission.ReviewedAt = now;
        submission.UpdatedAt = now;

        document.VerifierUserId = userId;
        document.VerifierSignedAt = now;
        document.VerifierChecks = HandoverDocumentRules.EncodeChecks(request.Checks);
        document.VerifierTermsVersion = HandoverDocumentRules.TermsVersion;
        document.Status = HandoverDocumentStatus.Completed;
        document.CompletedAt = now;
        document.UpdatedAt = now;

        turn.IdentityConfirmedByUserId = userId;
        turn.IdentityConfirmedAt = now;
        // Chạm kế hoạch để RowVersion của nó chặn việc này chạy cùng lúc với đặt hold hay báo còn sống.
        vault.Plan.UpdatedAt = now;

        // Kết thúc lượt ngay: không còn bước đồng ý hay từ chối riêng sau khi Người xét duyệt gửi.
        if (document.Kind == HandoverSubmissionKind.Accept)
        {
            HandoverGrant grant = HandoverRules.CommitAccept(turn, turn.ClaimantUserId.Value, now);
            handover.AddGrant(grant);
        }
        else
        {
            HandoverRules.MarkDeclined(turn, now);
        }
        List<HandoverTurn> turns = await handover.FindTurnsByPlanAsync(vault.Plan.Id, ct);
        HandoverRules.CloseIfAllTerminal(vault.Plan, turns, now);

        // Ảnh căn cước chỉ để đối chiếu nên xóa ngay khi biên bản hoàn tất: khóa trong bản ghi bị xóa trắng trước khi lưu.
        string frontKey = submission.FrontImageStorageKey;
        string backKey = submission.BackImageStorageKey;
        submission.FrontImageStorageKey = string.Empty;
        submission.BackImageStorageKey = string.Empty;
        submission.FrontImageWrappedDataKey = new byte[submission.FrontImageWrappedDataKey.Length];
        submission.BackImageWrappedDataKey = new byte[submission.BackImageWrappedDataKey.Length];

        string? paperKey = null;
        if (paperWriter != null)
        {
            paperKey = await paperWriter.WriteAsync(submission, turn, userId, now, ct, document);
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
        await IdentityImageStorage.TryDeleteAsync(contentStore, frontKey);
        await IdentityImageStorage.TryDeleteAsync(contentStore, backKey);

        if (notifier != null)
        {
            await notifier.CompletedAsync(document, ct);
        }
        return ToDetail(submission, document);
    }

    /// <summary>
    /// Dựng bù ảnh văn bản khi hồ sơ chưa có (nộp trước khi văn bản được dựng ngay lúc nộp, hoặc lúc nộp dựng lỗi).
    /// Trả khóa lưu để bù trừ khi lưu bản ghi thất bại; hồ sơ đã có ảnh hoặc thiếu bộ dựng thì bỏ qua.
    /// </summary>
    private async Task<string?> StoreMissingPaperAsync(
        HandoverSubmission submission, Guid reviewerId, DateTimeOffset now, CancellationToken ct)
    {
        if (paperWriter == null || submission.PaperStorageKey.Length > 0)
        {
            return null;
        }
        return await paperWriter.WriteAsync(submission, submission.Turn, reviewerId, now, ct);
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
        HandoverDocument? returnedDocument = null;
        if (documents != null)
        {
            returnedDocument = await documents.FindByTurnIdAsync(submission.TurnId, ct);
        }
        if (returnedDocument != null && returnedDocument.Status == HandoverDocumentStatus.WaitVerifier)
        {
            // Trả về người nhận: sửa Phần 2 và ký lại, Phần 1 (chép từ hồ sơ chứng tử) giữ nguyên.
            returnedDocument.Status = HandoverDocumentStatus.WaitRecipient;
            returnedDocument.SupplementReason = reason;
            returnedDocument.RecipientSignedAt = null;
            returnedDocument.UpdatedAt = now;
        }
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

    /// <summary>Chi tiết hồ sơ thuộc một biên bản: duyệt được khi biên bản đang chờ Người xét duyệt, không còn chặn theo kết quả đối chiếu tự động.</summary>
    private static VerifierHandoverSubmissionResponse ToDetail(HandoverSubmission submission, HandoverDocument document)
    {
        VerifierHandoverSubmissionResponse response = ToDetail(submission);
        response.DocumentId = document.Id;
        bool waiting = submission.Status == HandoverSubmissionStatus.Submitted
            && document.Status == HandoverDocumentStatus.WaitVerifier
            && submission.Turn.Status == HandoverTurnStatus.IdentityInProgress;
        response.CanApprove = waiting;
        response.CanRequestSupplement = waiting;
        return response;
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
