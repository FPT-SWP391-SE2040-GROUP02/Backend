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
    /// Luồng Biên Bản: Người xét duyệt kiểm tra thủ công (tick đủ hai mục và xác nhận trách nhiệm) rồi gửi.
    /// Hoàn tất biên bản và kết thúc lượt: Chấp nhận thì mở quyền tải tài sản, Từ chối thì ghi nhận từ chối.
    /// Lỗi: 400 chưa tick đủ; 404; 409 WRONG_STATE, NO_DOCUMENT, PLAN_NOT_IN_HANDOVER, HOLD_ACTIVE.
    /// </summary>
    Task<VerifierHandoverSubmissionResponse> CompleteAsync(
        Guid submissionId, ApproveHandoverDocumentRequest request, CancellationToken ct);

    /// <summary>Yêu cầu người nhận bổ sung kèm lý do; người nhận nộp lại bản mới.</summary>
    Task<VerifierHandoverSubmissionResponse> RequestSupplementAsync(
        Guid submissionId, RequestHandoverSupplementRequest request, CancellationToken ct);
}

// Người xét duyệt kiểm tra hồ sơ người nhận, các nút theo thứ tự trên màn /notary/recipients:
//   1. Danh sách hồ sơ chờ kiểm tra ........ ListPendingAsync
//   2. Mở một hồ sơ ........................ GetAsync
//   3. Xem ảnh căn cước mặt trước, mặt sau .. PrepareImageAsync
//   4. Gửi biên bản (luồng Biên Bản) ....... CompleteAsync: kết thúc lượt ngay
//   5. Yêu cầu bổ sung ..................... RequestSupplementAsync
// Luồng cũ, chỉ cho lượt không có biên bản: ApproveAsync (lượt sang chờ người nhận quyết định).
// Mọi nút đều qua LoadForVerifierAsync: chỉ Người xét duyệt đã duyệt hồ sơ chứng tử của kho mới thấy hồ sơ.
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

    // Bước 1: danh sách hồ sơ người nhận đang chờ Người xét duyệt này.
    public async Task<List<VerifierHandoverSubmissionSummaryResponse>> ListPendingAsync(CancellationToken ct)
    {
        // 1. Phải là Người xét duyệt đang hoạt động, rồi lấy các hồ sơ chờ của họ
        Guid userId = await RequireActiveVerifierAsync(ct);
        List<HandoverSubmission> pending = await submissions.ListPendingForVerifierAsync(userId, ct);
        // 2. Dựng một dòng tóm tắt cho mỗi hồ sơ
        List<VerifierHandoverSubmissionSummaryResponse> list = new List<VerifierHandoverSubmissionSummaryResponse>();
        foreach (HandoverSubmission submission in pending)
        {
            list.Add(submission.ToVerifierSummary());
        }
        return list;
    }

    // Bước 2: mở một hồ sơ để kiểm tra.
    public async Task<VerifierHandoverSubmissionResponse> GetAsync(Guid submissionId, CancellationToken ct)
    {
        // 1. Tìm hồ sơ; không thuộc Người xét duyệt này thì báo không tìm thấy
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        // Hồ sơ thuộc biên bản thì trả kèm Id biên bản (DocumentId) để FE lấy họ tên, số căn cước, năm sinh, nơi thường trú
        // của người nhận hiện cho Người xét duyệt đối chiếu với ảnh.
        if (documents != null)
        {
            HandoverDocument? document = await documents.FindByTurnIdAsync(submission.TurnId, ct);
            if (document != null)
            {
                return submission.ToVerifierDetail(document);
            }
        }
        return submission.ToVerifierDetail();
    }

    // Bước 3: xem ảnh căn cước một mặt (front hoặc back).
    public async Task<PreparedDownload> PrepareImageAsync(Guid submissionId, string side, CancellationToken ct)
    {
        // 1. Tìm hồ sơ và kiểm mặt ảnh hợp lệ
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

        // 2. Lấy thông tin ảnh đã lưu mã hóa rồi chuẩn bị giải mã để trả về
        StoredContent stored;
        string contentType;
        string fileName;
        if (front)
        {
            stored = new StoredContent
            {
                StorageKey = submission.FrontImageStorageKey,
                SizeBytes = submission.FrontImageSizeBytes,
                EncryptionKeyId = submission.FrontImageEncryptionKeyId,
                WrappedDataKey = submission.FrontImageWrappedDataKey
            };
            contentType = submission.FrontImageContentType;
            fileName = "cccd-mat-truoc";
        }
        else
        {
            stored = new StoredContent
            {
                StorageKey = submission.BackImageStorageKey,
                SizeBytes = submission.BackImageSizeBytes,
                EncryptionKeyId = submission.BackImageEncryptionKeyId,
                WrappedDataKey = submission.BackImageWrappedDataKey
            };
            contentType = submission.BackImageContentType;
            fileName = "cccd-mat-sau";
        }
        return await IdentityImageStorage.PrepareAsync(contentStore, stored, contentType, fileName, ct);
    }

    // Luồng cũ: duyệt hồ sơ của lượt không có biên bản; lượt sang chờ người nhận quyết định.
    public async Task<VerifierHandoverSubmissionResponse> ApproveAsync(Guid submissionId, CancellationToken ct)
    {
        // 1. Tìm hồ sơ và lượt của nó
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = submission.Turn;

        // 2. Các điều kiện: chưa duyệt, đang chờ duyệt, không thuộc biên bản, ba trường đều khớp,
        //    kế hoạch đang bàn giao, kho không bị giữ, lượt đang ở bước xác minh
        // Bấm lặp: đã duyệt rồi thì trả lại kết quả cũ.
        if (submission.Status == HandoverSubmissionStatus.Approved)
        {
            return submission.ToVerifierDetail();
        }
        if (submission.Status != HandoverSubmissionStatus.Submitted)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Hồ sơ này không còn ở trạng thái chờ duyệt.");
        }
        if (documents != null && await documents.FindByTurnIdAsync(turn.Id, ct) != null)
        {
            throw HandoverAccess.NotAllowed("USE_DOCUMENT", "Hồ sơ này thuộc một biên bản. Hãy kiểm tra thủ công rồi bấm gửi.");
        }
        if (!(submission.NameMatches && submission.CitizenIdMatches && submission.DateOfBirthMatches))
        {
            throw HandoverAccess.NotAllowed("FIELDS_MISMATCH", "Có trường không khớp thông tin Chủ kho đã khai. Chỉ yêu cầu bổ sung được.");
        }
        Vault vault = await RequireVaultInHandoverAsync(turn, ct);
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }

        // 3. Đánh dấu hồ sơ đã duyệt, lượt sang chờ quyết định, ghi người xác nhận danh tính
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

        // 4. Ghi nhật ký rồi lưu một lần; lưu lỗi thì xóa ảnh văn bản vừa dựng
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
        return submission.ToVerifierDetail();
    }

    // Bước 4: Người xét duyệt tick đủ các mục kiểm tra, xác nhận trách nhiệm và bấm gửi biên bản.
    public async Task<VerifierHandoverSubmissionResponse> CompleteAsync(
        Guid submissionId, ApproveHandoverDocumentRequest request, CancellationToken ct)
    {
        // 1. Tìm hồ sơ, lượt và biên bản của lượt; không có biên bản thì không hoàn tất được
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
            throw HandoverAccess.NotAllowed("NO_DOCUMENT", "Hồ sơ này không có biên bản để hoàn tất.");
        }

        // 2. Các điều kiện: biên bản đang chờ Người xét duyệt và đúng hồ sơ này, tick đủ, đã xác nhận,
        //    lượt đang ở bước lập biên bản, kế hoạch đang bàn giao, kho không bị giữ
        // Bấm lặp: đã hoàn tất rồi thì trả lại kết quả cũ.
        if (document.Status == HandoverDocumentStatus.Completed && submission.Status == HandoverSubmissionStatus.Approved)
        {
            return submission.ToVerifierDetail(document);
        }
        if (submission.Status != HandoverSubmissionStatus.Submitted
            || document.Status != HandoverDocumentStatus.WaitVerifier
            || document.SubmissionId != submission.Id)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Biên bản này không còn chờ bạn kiểm tra.");
        }
        if (!HandoverDocumentRules.AllChecked(request.Checks))
        {
            throw ValidationErrors.For("checks", "Phải tick đủ cả hai mục kiểm tra thủ công.");
        }
        if (!request.Acknowledged)
        {
            throw ValidationErrors.For("acknowledged", "Vui lòng tick xác nhận trách nhiệm để gửi.");
        }
        if (turn.Status != HandoverTurnStatus.IdentityInProgress || turn.ClaimantUserId == null)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không ở bước lập biên bản.");
        }
        Vault vault = await RequireVaultInHandoverAsync(turn, ct);

        // 3. Đánh dấu hồ sơ đã duyệt, biên bản hoàn tất, ghi người xác nhận danh tính
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

        // 4. Kết thúc lượt: Chấp nhận thì tạo quyền tải, Từ chối thì ghi nhận từ chối; đóng kế hoạch nếu mọi lượt đã xong
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

        // 5. Xóa khóa ảnh căn cước khỏi bản ghi, dựng ảnh văn bản đã ký
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
            HandoverPaperRequest paperRequest = new HandoverPaperRequest
            {
                Submission = submission,
                Turn = turn,
                ReviewerId = userId,
                ReviewedAt = now,
                Document = document
            };
            paperKey = await paperWriter.WriteAsync(paperRequest, ct);
        }

        // 6. Ghi nhật ký rồi lưu một lần; lưu lỗi thì xóa ảnh văn bản vừa dựng
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
        // 7. Sau khi lưu: xóa tệp ảnh căn cước, báo các bên, trả kết quả
        await IdentityImageStorage.TryDeleteAsync(contentStore, frontKey);
        await IdentityImageStorage.TryDeleteAsync(contentStore, backKey);

        if (notifier != null)
        {
            await notifier.CompletedAsync(document, ct);
        }
        return submission.ToVerifierDetail(document);
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
        HandoverPaperRequest paperRequest = new HandoverPaperRequest
        {
            Submission = submission,
            Turn = submission.Turn,
            ReviewerId = reviewerId,
            ReviewedAt = now
        };
        return await paperWriter.WriteAsync(paperRequest, ct);
    }

    // Bước 5: Người xét duyệt yêu cầu người nhận bổ sung, kèm lý do.
    public async Task<VerifierHandoverSubmissionResponse> RequestSupplementAsync(
        Guid submissionId, RequestHandoverSupplementRequest request, CancellationToken ct)
    {
        // 1. Lý do không được để trống
        string reason = request.Reason.Trim();
        if (reason.Length == 0)
        {
            throw ValidationErrors.For("reason", "Vui lòng nhập lý do yêu cầu bổ sung.");
        }

        // 2. Tìm hồ sơ; hồ sơ phải đang chờ duyệt và lượt đang ở bước xác minh
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        Guid userId = currentUser.RequireUserId();
        if (submission.Status != HandoverSubmissionStatus.Submitted)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Hồ sơ này không còn ở trạng thái chờ duyệt.");
        }
        if (submission.Turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }

        // 3. Đánh dấu hồ sơ cần bổ sung; biên bản (nếu có) trả về người nhận để sửa Phần 2 và ký lại
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

        // 4. Ghi nhật ký, lưu một lần, trả kết quả
        auditLogs.Add(AuditRules.Create(
            submission.Turn.VaultId, userId, AuditActorRole.Verifier, AuditAction.HandoverSubmissionSupplementRequested,
            AuditTargetType.HandoverTurn, submission.Turn.Id, null, AuditOutcome.Succeeded, null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return submission.ToVerifierDetail();
    }

    /// <summary>Người đang đăng nhập phải là Người xét duyệt còn hoạt động; không thì ném lỗi phiên hết hạn, tài khoản bị khóa hoặc sai vai trò. Trả về mã người dùng.</summary>
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

    /// <summary>Verifier hợp lệ và là người đã duyệt hồ sơ chứng tử của kho; không thì 404 như hồ sơ không tồn tại.</summary>
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

    /// <summary>Kho của lượt phải còn trong giai đoạn bàn giao và không bị giữ; không thì ném lỗi không được phép.</summary>
    private async Task<Vault> RequireVaultInHandoverAsync(HandoverTurn turn, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw SubmissionNotFound();
        }
        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw HandoverAccess.NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw HandoverAccess.NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa duyệt được.");
        }
        return vault;
    }

    /// <summary>Lỗi 404 dùng chung cho hồ sơ không có, hồ sơ của kho khác và hồ sơ Người xét duyệt không được xem.</summary>
    private static AppException SubmissionNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy hồ sơ người nhận.");
    }
}
