using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// V1: mọi kiểm "Verifier được giao" đọc phân công ĐANG HIỆU LỰC (EndedAt null) của chính tôi.
/// SEC-04, SEC-05: hồ sơ không có hoặc không giao cho tôi luôn là 404 giống nhau.
/// DATA-02, SEC-07: không ghi CCCD, họ tên, số giấy, khóa lưu trữ vào log hay message lỗi.
/// Mỗi use case ghi gọi SaveChanges một lần (STATE-02).
/// </remarks>
public sealed class DeathCaseReviewService : IDeathCaseReviewService
{
    private const string SortByDueAt = "dueAt";
    private const string SortByAssignedAt = "assignedAt";

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository userRepository;
    private readonly IVaultRepository vaultRepository;
    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IDeathCaseReviewRepository reviewRepository;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverStarter handoverStarter;
    private readonly IDeathCaseDataProtector dataProtector;
    private readonly IAssetContentStore contentStore;
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IDeathCaseVerifierPicker verifierPicker;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IDeathCaseExecutorNoticeSender executorNoticeSender;
    private readonly IOwnerIdentityRepository ownerIdentityRepository;
    private readonly IOwnerIdentityProtector ownerIdentityProtector;

    public DeathCaseReviewService(
        ICurrentUser currentUser,
        IUserRepository userRepository,
        IVaultRepository vaultRepository,
        IDeathCaseRepository deathCaseRepository,
        IDeathCaseReviewRepository reviewRepository,
        IExecutorReader executorReader,
        IHandoverStarter handoverStarter,
        IDeathCaseDataProtector dataProtector,
        IAssetContentStore contentStore,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IDeathCaseVerifierPicker verifierPicker,
        IAuditLogRepository auditLogRepository,
        IDeathCaseExecutorNoticeSender executorNoticeSender,
        IOwnerIdentityRepository ownerIdentityRepository,
        IOwnerIdentityProtector ownerIdentityProtector)
    {
        this.currentUser = currentUser;
        this.userRepository = userRepository;
        this.vaultRepository = vaultRepository;
        this.deathCaseRepository = deathCaseRepository;
        this.reviewRepository = reviewRepository;
        this.executorReader = executorReader;
        this.handoverStarter = handoverStarter;
        this.dataProtector = dataProtector;
        this.contentStore = contentStore;
        this.holdReader = holdReader;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.verifierPicker = verifierPicker;
        this.auditLogRepository = auditLogRepository;
        this.executorNoticeSender = executorNoticeSender;
        this.ownerIdentityRepository = ownerIdentityRepository;
        this.ownerIdentityProtector = ownerIdentityProtector;
    }

    public async Task<PaginatedList<VerifierDeathCaseSummaryResponse>> ListAsync(PaginationRequest pagination, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        await EnsureActiveVerifierAsync(userId, ct);

        // Chuẩn hóa về đúng chuỗi repository so sánh chính xác; giá trị lạ → 400.
        string sortBy = SortByDueAt;
        if (!string.IsNullOrWhiteSpace(pagination.SortBy))
        {
            string requested = pagination.SortBy.Trim();
            if (string.Equals(requested, SortByDueAt, StringComparison.OrdinalIgnoreCase))
            {
                sortBy = SortByDueAt;
            }
            else if (string.Equals(requested, SortByAssignedAt, StringComparison.OrdinalIgnoreCase))
            {
                sortBy = SortByAssignedAt;
            }
            else
            {
                throw ValidationErrors.For("sortBy", "Chỉ sắp xếp theo dueAt hoặc assignedAt.");
            }
        }

        // searchTerm bị bỏ qua: họ tên Owner đã mã hóa, danh sách chỉ có 4 số cuối CCCD.
        (List<VerifierCaseRow> Items, int TotalCount) page = await reviewRepository.FindAssignedPageAsync(
            userId, sortBy, pagination.IsAscending, pagination.PageIndex, pagination.PageSize, ct);

        List<VerifierDeathCaseSummaryResponse> items = new List<VerifierDeathCaseSummaryResponse>();
        foreach (VerifierCaseRow row in page.Items)
        {
            items.Add(row.ToResponse());
        }
        return items.ToPaginatedList(page.TotalCount, pagination);
    }

    public async Task<VerifierDeathCaseResponse> GetAsync(Guid caseId, CancellationToken ct)
    {
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);
        return await BuildResponseAsync(context, null, ct);
    }

    public async Task<PreparedDeathCaseFile> PrepareFileAsync(Guid caseId, Guid fileId, CancellationToken ct)
    {
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);

        // DEATH-04: chỉ trang của bản đã gửi của đúng hồ sơ này; bản nháp hoặc hồ sơ khác → 404.
        DeathCaseFile? file = await reviewRepository.FindSubmittedFileAsync(caseId, fileId, ct);
        if (file == null)
        {
            throw CaseNotFound();
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = file.StorageKey;
        stored.SizeBytes = file.SizeBytes;
        stored.Sha256 = file.Sha256;
        stored.EncryptionKeyId = file.EncryptionKeyId;
        stored.WrappedDataKey = file.WrappedDataKey;

        // DATA-06: lượt 1 giải mã vào Stream.Null. Việc kiểm kích thước + SHA-256 chỉ xảy ra ở CUỐI luồng,
        // nên phải chạy hết một lượt trước khi gửi byte đầu tiên, nếu không Verifier có thể nhận tệp hỏng.
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Nội dung trang giấy chứng tử không vượt qua kiểm tra toàn vẹn.", ex);
        }

        string fileName = file.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = DeathCaseFileRules.DefaultFileName;
        }

        PreparedDeathCaseFile prepared = new PreparedDeathCaseFile();
        prepared.CaseId = caseId;
        prepared.FileId = fileId;
        prepared.ViewerUserId = context.UserId;
        prepared.FileName = fileName;
        prepared.ContentType = file.ContentType;
        prepared.Length = file.SizeBytes;
        // Lượt 2: giải mã lần nữa, lần này ghi thẳng ra response.
        prepared.WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token);
        return prepared;
    }

    public async Task<PreparedDownload> PrepareOwnerDocumentImageAsync(Guid caseId, CancellationToken ct)
    {
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);
        OwnerIdentity? identity = await FindOwnerIdentityAsync(context.DeathCase, ct);
        if (identity == null || identity.DocumentImageStorageKey == null || identity.DocumentImageWrappedDataKey == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.IdentityImageNotFound,
                "Chủ kho chưa có ảnh CCCD lưu lúc xác minh danh tính.");
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = identity.DocumentImageStorageKey;
        stored.SizeBytes = identity.DocumentImageSizeBytes ?? 0;
        stored.Sha256 = identity.DocumentImageSha256 ?? string.Empty;
        stored.EncryptionKeyId = identity.DocumentImageEncryptionKeyId ?? string.Empty;
        stored.WrappedDataKey = identity.DocumentImageWrappedDataKey;

        string contentType = identity.DocumentImageContentType ?? "application/octet-stream";
        return await IdentityImageStorage.PrepareAsync(contentStore, stored, contentType, "cccd-mat-truoc", ct);
    }

    public async Task<VerifierDeathCaseResponse> ReviewFlagAsync(
        Guid caseId, Guid flagId, ReviewDeathCaseFlagRequest request, CancellationToken ct)
    {
        // 1) Chuẩn hóa input trước (DEATH-10: ghi chú bắt buộc).
        string note = DeathCaseReviewRules.NormalizeReason(request.Note, "note");

        // 2) Verifier + phân công đang hiệu lực.
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;

        // 3) Chỉ cờ của bản đang xét; cờ của bản cũ hoặc hồ sơ khác → 404.
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(deathCase);
        DeathCaseFlag? flag = null;
        foreach (DeathCaseFlag candidate in deathCase.Flags)
        {
            if (candidate.Id == flagId && candidate.VersionId == reviewVersion.Id)
            {
                flag = candidate;
            }
        }
        if (flag == null)
        {
            throw CaseNotFound();
        }

        // 4) Gọi lặp: cờ đã xem xét thì giữ ghi chú đầu, trả trạng thái hiện tại.
        if (flag.ReviewedAt != null)
        {
            return await BuildResponseAsync(context, null, ct);
        }

        // 5) Hồ sơ đã đóng, 6) etag, 7) trạng thái, 8) xung đột lợi ích.
        EnsureNotClosed(deathCase);
        EnsureEtag(request.Etag, deathCase);
        EnsureCanDecide(deathCase);
        await EnsureNoConflictOfInterestAsync(context, ct);

        // 9) Ghi một lần; UpdatedAt đổi nên etag đổi (V5).
        DateTimeOffset now = timeProvider.GetUtcNow();
        flag.ReviewedByUserId = context.UserId;
        flag.ReviewedAt = now;
        flag.ReviewNote = note;
        deathCase.UpdatedAt = now;
        // SEC-07: nhật ký lưu cùng lần lưu; không ghi ghi chú tự do.
        AddAudit(context, AuditAction.DeathCaseFlagReviewed, null, now);
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildResponseAsync(context, null, ct);
    }

    public async Task<VerifierDeathCaseResponse> RequestSupplementAsync(
        Guid caseId, RequestDeathCaseSupplementRequest request, CancellationToken ct)
    {
        // 1) Chuẩn hóa input trước (DEATH-01).
        string reason = DeathCaseReviewRules.NormalizeReason(request.Reason, "reason");
        string requestedParts = DeathCaseReviewRules.NormalizeRequestedParts(request.RequestedParts);

        // 2) Verifier + phân công đang hiệu lực.
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;

        // 3) Gọi lặp cùng nội dung: trả trạng thái hiện tại, không thêm dòng.
        if (IsRepeatedSupplement(context, reason, requestedParts))
        {
            return await BuildResponseAsync(context, null, ct);
        }

        // 4) Hồ sơ đã đóng, 5) etag, 6) trạng thái, 7) xung đột lợi ích.
        EnsureNotClosed(deathCase);
        EnsureEtag(request.Etag, deathCase);
        EnsureCanDecide(deathCase);
        await EnsureNoConflictOfInterestAsync(context, ct);

        // 8) Ghi một lần. Phân công, kế hoạch và đồng hồ 72 giờ giữ nguyên. Được làm khi Executor đang có bản nháp.
        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCaseDecision decision = NewDecision(context, DeathCaseDecisionKind.SupplementRequested, reason, now);
        decision.RequestedParts = requestedParts;
        decision.DueAt = now.AddDays(DeathCaseReviewRules.SupplementDueDays);
        deathCase.Decisions.Add(decision);
        reviewRepository.AddDecision(decision);
        deathCase.Status = DeathCaseStatus.SupplementRequired;
        deathCase.UpdatedAt = now;
        // SEC-07: Reason chỉ chứa mã phần cần bổ sung đã chuẩn hóa, không chép lý do tự do (Owner xem được nhật ký).
        AddAudit(context, AuditAction.DeathCaseSupplementRequested, requestedParts, now);
        await unitOfWork.SaveChangesAsync(ct);
        // Báo Executor sau khi đã lưu: gửi thư lỗi không làm hỏng quyết định đã ghi.
        await NotifyExecutorsAsync(deathCase, DeathCaseDecisionKind.SupplementRequested, decision.DueAt, ct);
        return await BuildResponseAsync(context, null, ct);
    }

    public async Task<VerifierDeathCaseResponse> RejectAsync(
        Guid caseId, RejectDeathCaseRequest request, CancellationToken ct)
    {
        // 1) Chuẩn hóa input trước (DEATH-12: lý do bắt buộc).
        string reason = DeathCaseReviewRules.NormalizeReason(request.Reason, "reason");

        // 2) Verifier + phân công đang hiệu lực.
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;

        // 3) Hồ sơ đã đóng (kiểm TRƯỚC etag): đã từ chối rồi thì gọi lặp trả trạng thái hiện tại (không thêm dòng);
        //    đã duyệt thì không từ chối được nữa (AC-71).
        if (deathCase.Status == DeathCaseStatus.Rejected)
        {
            return await BuildResponseAsync(context, null, ct);
        }
        EnsureNotClosed(deathCase);

        // 4) etag, 5) trạng thái, 6) xung đột lợi ích.
        EnsureEtag(request.Etag, deathCase);
        EnsureCanDecide(deathCase);
        await EnsureNoConflictOfInterestAsync(context, ct);

        // 7) DEATH-12: kế hoạch phải đang khóa DeathCaseOpen và hồ sơ phải nhớ trạng thái trước đó.
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        // AC-71: hồ sơ và kế hoạch không được đọc cùng một lúc. Verifier khác có thể vừa từ chối/duyệt xong ngay sau lúc
        // ta đọc hồ sơ (kế hoạch khi đó không còn DEATH_CASE_OPEN), nên kiểm sai thì ĐỌC LẠI hồ sơ trước khi báo lỗi:
        // đã bị từ chối → 200, đã được duyệt → 409 CASE_CLOSED, còn lại → PLAN_STATE_INVALID như cũ.
        if (vault == null || vault.Plan.Status != PlanStatus.DeathCaseOpen || deathCase.StatusBeforeCase == null)
        {
            VerifierDeathCaseResponse? current = await TryResolveDecisionConflictAsync(caseId, DeathCaseStatus.Rejected, ct);
            if (current != null)
            {
                return current;
            }
            throw NotAllowed(DeathCaseNotAllowedReason.PlanStateInvalid);
        }
        HandoverPlan plan = vault.Plan;

        // 8) Ghi trong MỘT lần lưu: quyết định, đóng hồ sơ, trả kế hoạch về trạng thái trước, hủy thông báo đang chờ.
        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCaseDecision decision = NewDecision(context, DeathCaseDecisionKind.Rejected, reason, now);
        deathCase.Decisions.Add(decision);
        reviewRepository.AddDecision(decision);
        deathCase.Status = DeathCaseStatus.Rejected;
        deathCase.ClosedAt = now;
        deathCase.UpdatedAt = now;

        // DEATH-12, NFR-03: chỉ trả trạng thái kế hoạch; KHÔNG sửa kỳ điểm danh. CheckInJob tính lại theo kỳ
        // (ACTIVE → CHECKIN_OVERDUE → AWAITING_CHECK) trong vòng 60 giây.
        plan.Status = deathCase.StatusBeforeCase.Value;
        plan.UpdatedAt = now;

        // DEATH-08: thông báo còn Pending không được gửi nữa (job bỏ qua mọi thông báo khác Pending).
        // Failed/Delivered giữ nguyên. Không gửi email khi từ chối. "Nhả snapshot" (§11.2): snapshot ghim lúc duyệt
        // nên chưa có gì để nhả.
        foreach (DeathCaseNotice notice in deathCase.Notices)
        {
            if (notice.Status == DeathCaseNoticeStatus.Pending)
            {
                notice.Status = DeathCaseNoticeStatus.Cancelled;
                notice.UpdatedAt = now;
            }
        }

        // SEC-07: nhật ký lưu cùng lần lưu; bên thua tranh chấp bị DiscardPendingChanges nên dòng bị bỏ.
        AddAudit(context, AuditAction.DeathCaseRejected, null, now);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsSaveRace(ex))
        {
            // AC-71: Verifier khác (hoặc lần bấm trước) vừa từ chối xong → trả trạng thái hiện tại; còn lại ném lại lỗi gốc.
            VerifierDeathCaseResponse? current = await TryResolveDecisionConflictAsync(caseId, DeathCaseStatus.Rejected, ct);
            if (current != null)
            {
                return current;
            }
            throw;
        }
        // Báo Executor sau khi đã lưu, chỉ khi chính lần gọi này từ chối (lần gọi lặp đã trả về ở trên).
        await NotifyExecutorsAsync(deathCase, DeathCaseDecisionKind.Rejected, null, ct);
        return await BuildResponseAsync(context, null, ct);
    }

    public async Task<VerifierDeathCaseResponse> ApproveAsync(
        Guid caseId, ApproveDeathCaseRequest request, CancellationToken ct)
    {
        // 1) Chuẩn hóa input trước, rồi Verifier + phân công đang hiệu lực (V3).
        string reason = DeathCaseReviewRules.NormalizeReason(request.Reason, "reason");
        ReviewContext context = await LoadCaseForVerifierAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;

        // 2) AC-71: hồ sơ đã đóng (kiểm TRƯỚC etag). Đã duyệt rồi thì gọi lặp trả trạng thái hiện tại
        //    (không thêm dòng, không gọi lại StartAsync); đã bị từ chối thì 409 CASE_CLOSED.
        if (deathCase.Status == DeathCaseStatus.ApprovedForHandover)
        {
            return await BuildResponseAsync(context, null, ct);
        }
        EnsureNotClosed(deathCase);

        // 3) STATE-03: etag.
        EnsureEtag(request.Etag, deathCase);

        // 4) DEATH-04, 08, 09, 10: còn lý do chưa duyệt được thì 409, reason = lý do đầu, approveBlockers = cả danh sách.
        //    Sau đó ROLE-03 (VERIFIER_CONFLICT).
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, timeProvider.GetUtcNow());
        if (blockers.Count > 0)
        {
            Dictionary<string, object?> blockerExtensions = new Dictionary<string, object?>();
            blockerExtensions["reason"] = blockers[0];
            blockerExtensions["approveBlockers"] = blockers;
            throw new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseNotAllowed,
                "Hồ sơ chứng tử chưa duyệt được.", blockerExtensions);
        }
        await EnsureNoConflictOfInterestAsync(context, ct);

        // 5) Kế hoạch phải đang khóa DEATH_CASE_OPEN (lúc Executor gửi lần đầu).
        //    AC-71: hồ sơ và kế hoạch không được đọc cùng một lúc. Verifier khác có thể vừa duyệt/từ chối xong ngay sau
        //    lúc ta đọc hồ sơ (kế hoạch khi đó không còn DEATH_CASE_OPEN), nên kế hoạch sai thì phải ĐỌC LẠI hồ sơ
        //    chứ chưa kết luận PLAN_STATE_INVALID: đã duyệt → 200, đã bị từ chối → 409 CASE_CLOSED, còn lại → PLAN_STATE_INVALID.
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null || vault.Plan.Status != PlanStatus.DeathCaseOpen)
        {
            return await ResolveApproveConflictAsync(caseId, ct);
        }
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            // SRS §6.4: hold đang hiệu lực chặn duyệt (kể cả hold do Admin đặt).
            throw NotAllowed(DeathCaseNotAllowedReason.HoldActive);
        }

        // 6) Đánh dấu duyệt NHƯNG CHƯA LƯU: quyết định, đóng hồ sơ. Phân công giữ nguyên.
        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCaseDecision decision = NewDecision(context, DeathCaseDecisionKind.Approved, reason, now);
        deathCase.Decisions.Add(decision);
        reviewRepository.AddDecision(decision);
        deathCase.Status = DeathCaseStatus.ApprovedForHandover;
        deathCase.ClosedAt = now;
        deathCase.UpdatedAt = now;
        // SEC-07: đặt TRƯỚC StartAsync để lần lưu duy nhất trong StartAsync lưu luôn dòng này. Nhánh HandoverNotAllowed
        // và các nhánh tranh chấp đều bỏ thay đổi chưa lưu nên dòng không được ghi.
        AddAudit(context, AuditAction.DeathCaseApproved, null, now);

        // 7) DEATH-06, V6: ApproveAsync KHÔNG tự SaveChanges. Lần lưu duy nhất là lần đầu bên trong StartAsync
        //    (cùng một lần lưu: quyết định + hồ sơ + lượt bàn giao + snapshot + kế hoạch). Không cấp grant, không giải mã
        //    tài sản: StartAsync chỉ tạo lượt Invited.
        HandoverStartResult result;
        try
        {
            result = await handoverStarter.StartAsync(deathCase.VaultId, ct);
        }
        catch (AppException ex) when (ex.Code == ErrorCode.HandoverNotAllowed)
        {
            // Ví dụ NOTHING_TO_HAND_OVER: StartAsync ném TRƯỚC khi lưu → bỏ thay đổi chưa lưu, hồ sơ vẫn đang xét.
            deathCaseRepository.DiscardPendingChanges();
            throw;
        }
        catch (Exception ex) when (IsSaveRace(ex))
        {
            // AC-71: Verifier khác vừa duyệt, hoặc Owner/job vừa đổi kế hoạch (EDIT_CONFLICT, trùng lượt/quyết định).
            return await ResolveApproveConflictAsync(caseId, ct);
        }

        // Kế hoạch đã có lượt bàn giao từ trước: StartAsync không lưu gì, các thay đổi ở bước 6 vẫn chưa được lưu.
        if (result.AlreadyStarted)
        {
            return await ResolveApproveConflictAsync(caseId, ct);
        }

        VerifierHandoverResponse handover = new VerifierHandoverResponse();
        handover.TurnCount = result.TurnCount;
        handover.InvitationsSent = result.InvitationsSent;
        handover.InvitationsFailed = result.InvitationsFailed;
        return await BuildResponseAsync(context, handover, ct);
    }

    /// <summary>
    /// ROLE-04: đọc tài khoản từ DB, không tin claim role trong token.
    /// Không có tài khoản → 401 SESSION_EXPIRED; không Active → 403 ACCOUNT_NOT_ACTIVE; không phải Verifier → 403 ROLE_NOT_ALLOWED.
    /// </summary>
    private async Task EnsureActiveVerifierAsync(Guid userId, CancellationToken ct)
    {
        User? user = await userRepository.FindByIdAsync(userId, ct);
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
                "Chỉ Verifier mới xét được hồ sơ chứng tử.");
        }
    }

    /// <summary>
    /// V3, V1: Verifier hợp lệ + hồ sơ mà tôi có phân công ĐANG HIỆU LỰC. Không có hồ sơ, hoặc không giao cho tôi,
    /// hoặc phân công đã kết thúc → 404 DEATH_CASE_NOT_FOUND (SEC-04, SEC-05).
    /// </summary>
    private async Task<ReviewContext> LoadCaseForVerifierAsync(Guid caseId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        await EnsureActiveVerifierAsync(userId, ct);

        DeathCase? deathCase = await deathCaseRepository.FindByIdAsync(caseId, ct);
        if (deathCase == null)
        {
            throw CaseNotFound();
        }

        DeathCaseAssignment? mine = null;
        foreach (DeathCaseAssignment assignment in deathCase.Assignments)
        {
            if (assignment.VerifierUserId == userId && assignment.EndedAt == null)
            {
                mine = assignment;
            }
        }
        if (mine == null)
        {
            // Hồ sơ chưa có phân công hiệu lực và đang ở trạng thái xem xét được: tự động gán cho Verifier đang mở.
            if (!deathCase.Assignments.Any(a => a.EndedAt == null) && DeathCaseReviewRules.CanDecide(deathCase.Status))
            {
                bool conflict = await HasConflictOfInterestAsync(deathCase, userId, ct);
                if (conflict)
                {
                    throw CaseNotFound();
                }

                DateTimeOffset now = timeProvider.GetUtcNow();
                mine = new DeathCaseAssignment
                {
                    CaseId = deathCase.Id,
                    VerifierUserId = userId,
                    AssignedAt = now,
                    DueAt = DeathCaseAssignmentRules.ComputeDueAt(now, deathCase.ObjectionEndsAt),
                };
                deathCase.Assignments.Add(mine);
                deathCaseRepository.AddAssignment(mine);
                await unitOfWork.SaveChangesAsync(ct);
            }
            else
            {
                throw CaseNotFound();
            }
        }

        ReviewContext context = new ReviewContext();
        context.DeathCase = deathCase;
        context.Assignment = mine;
        context.UserId = userId;
        return context;
    }

    /// <summary>Giải mã CHỈ bản đang xét, nạp hồ sơ trước đó rồi dựng response (kèm lý do chưa duyệt được).</summary>
    private async Task<VerifierDeathCaseResponse> BuildResponseAsync(
        ReviewContext context, VerifierHandoverResponse? handover, CancellationToken ct)
    {
        DeathCase deathCase = context.DeathCase;
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(deathCase);
        DeathCaseIdentityData? identity = dataProtector.Unprotect(reviewVersion);

        PreviousCaseRow? previous = null;
        if (deathCase.PreviousCaseId != null)
        {
            previous = await reviewRepository.FindPreviousCaseAsync(deathCase.PreviousCaseId.Value, ct);
        }

        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, timeProvider.GetUtcNow());
        // ROLE-03: GetApproveBlockers giữ thuần; xung đột lợi ích cần đọc kho nên thêm ở đây, cuối danh sách,
        // để FE không hiện nút duyệt với Verifier sẽ bị chặn lúc ghi.
        bool conflict = await HasConflictOfInterestAsync(deathCase, context.UserId, ct);
        if (conflict)
        {
            blockers.Add(DeathCaseNotAllowedReason.VerifierConflict.ToApiString());
        }
        VerifierDeathCaseResponse response = deathCase.ToVerifierResponse(context.Assignment, identity, previous, blockers, handover);
        response.OwnerIdentity = await BuildOwnerIdentityAsync(deathCase, identity, ct);
        response.Executor = await BuildExecutorInfoAsync(deathCase, reviewVersion, ct);
        return response;
    }

    /// <summary>Nạp thông tin Người thực hiện (Executor) đã nộp bản yêu cầu.</summary>
    private async Task<VerifierExecutorResponse?> BuildExecutorInfoAsync(
        DeathCase deathCase, DeathCaseVersion reviewVersion, CancellationToken ct)
    {
        if (reviewVersion.SubmittedByUserId == null || reviewVersion.SubmittedByUserId == Guid.Empty)
        {
            return null;
        }

        User? user = await userRepository.FindByIdAsync(reviewVersion.SubmittedByUserId.Value, ct);
        if (user == null)
        {
            return null;
        }

        string roleDesc = "Người thừa hành";
        List<Executor> executors = await executorReader.GetActiveAsync(deathCase.VaultId, ct);
        Executor? matchedExecutor = executors.FirstOrDefault(e => e.UserId == user.Id);
        if (matchedExecutor != null)
        {
            if (matchedExecutor.Role == ExecutorRole.Primary)
            {
                roleDesc = matchedExecutor.IsBeneficiary
                    ? "Người thừa hành chính (đồng thời là Người nhận)"
                    : "Người thừa hành chính";
            }
            else if (matchedExecutor.Role == ExecutorRole.Backup)
            {
                roleDesc = matchedExecutor.IsBeneficiary
                    ? "Người thừa hành dự phòng (đồng thời là Người nhận)"
                    : "Người thừa hành dự phòng";
            }
            else if (matchedExecutor.IsBeneficiary)
            {
                roleDesc = "Người thừa hành (đồng thời là Người nhận)";
            }
        }

        VerifierExecutorResponse executorResponse = new VerifierExecutorResponse();
        executorResponse.Id = user.Id;
        executorResponse.FullName = user.FullName;
        executorResponse.Email = user.Email;
        executorResponse.Phone = user.Phone;
        executorResponse.Role = roleDesc;
        return executorResponse;
    }

    /// <summary>
    /// Danh tính Owner đã gắn qua eKYC, giải mã để Verifier đối chiếu với bản Executor khai (BR-48 sửa 25/10/2026).
    /// So CCCD đủ 12 số và họ tên theo cùng cách chuẩn hóa của eKYC (NFC, không phân biệt hoa thường, giữ dấu).
    /// </summary>
    private async Task<VerifierOwnerIdentityResponse> BuildOwnerIdentityAsync(
        DeathCase deathCase, DeathCaseIdentityData? declared, CancellationToken ct)
    {
        VerifierOwnerIdentityResponse result = new VerifierOwnerIdentityResponse();
        OwnerIdentity? identity = await FindOwnerIdentityAsync(deathCase, ct);
        if (identity == null)
        {
            return result;
        }

        OwnerIdentityData verified = ownerIdentityProtector.Unprotect(identity);
        result.Verified = true;
        result.CitizenId = verified.CitizenId;
        result.FullName = verified.FullName;
        result.DateOfBirth = verified.DateOfBirth;
        result.VerifiedAt = identity.CreatedAt;
        result.HasDocumentImage = identity.DocumentImageStorageKey != null;
        result.DocumentImageCapturedAt = identity.DocumentImageCapturedAt;

        if (declared != null && !string.IsNullOrEmpty(declared.OwnerCitizenId))
        {
            result.CitizenIdMatches = string.Equals(declared.OwnerCitizenId, verified.CitizenId, StringComparison.Ordinal);
        }
        if (declared != null && !string.IsNullOrWhiteSpace(declared.OwnerFullName))
        {
            result.FullNameMatches = string.Equals(
                EkycRules.NormalizeName(declared.OwnerFullName), EkycRules.NormalizeName(verified.FullName),
                StringComparison.Ordinal);
        }
        return result;
    }

    /// <summary>Danh tính đã gắn của Owner kho; null khi Owner chưa xác minh hoặc không còn kho.</summary>
    private async Task<OwnerIdentity?> FindOwnerIdentityAsync(DeathCase deathCase, CancellationToken ct)
    {
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null)
        {
            return null;
        }
        return await ownerIdentityRepository.FindByUserIdAsync(vault.OwnerId, ct);
    }

    /// <summary>Lỗi lưu do request khác ghi trước: xung đột phiên bản (EDIT_CONFLICT) hoặc vi phạm chỉ mục unique.</summary>
    private bool IsSaveRace(Exception exception)
    {
        AppException? appException = exception as AppException;
        if (appException != null && appException.Code == ErrorCode.EditConflict)
        {
            return true;
        }
        return deathCaseRepository.IsUniqueViolation(exception);
    }

    /// <summary>
    /// Luật bên thua (AC-71), dùng khi lưu lỗi do tranh chấp HOẶC khi kiểm kế hoạch thấy sai: bỏ thay đổi chưa lưu,
    /// đọc lại hồ sơ. Hồ sơ đã ở trạng thái expected (bên kia thắng với cùng quyết định) → trả trạng thái hiện tại.
    /// Hồ sơ đã đóng bằng quyết định KHÁC → 409 CASE_CLOSED. Còn lại → null để người gọi tự xử lý (ném lỗi gốc).
    /// </summary>
    private async Task<VerifierDeathCaseResponse?> TryResolveDecisionConflictAsync(
        Guid caseId, DeathCaseStatus expected, CancellationToken ct)
    {
        deathCaseRepository.DiscardPendingChanges();
        ReviewContext latest = await LoadCaseForVerifierAsync(caseId, ct);
        if (latest.DeathCase.Status == expected)
        {
            return await BuildResponseAsync(latest, null, ct);
        }
        // Verifier khác vừa duyệt/từ chối bằng quyết định khác: không phải lỗi kế hoạch mà là hồ sơ đã đóng.
        EnsureNotClosed(latest.DeathCase);
        return null;
    }

    /// <summary>
    /// Luật bên thua khi duyệt (AC-71): bỏ thay đổi chưa lưu, đọc lại hồ sơ. Đã ApprovedForHandover (bên kia duyệt xong)
    /// → 200 trạng thái hiện tại, handover = null. Còn lại (kế hoạch đã có lượt mà hồ sơ chưa duyệt, hoặc kế hoạch
    /// bị đổi) → 409 PLAN_STATE_INVALID, hồ sơ vẫn đang xét. Không SaveChanges.
    /// </summary>
    private async Task<VerifierDeathCaseResponse> ResolveApproveConflictAsync(Guid caseId, CancellationToken ct)
    {
        VerifierDeathCaseResponse? current = await TryResolveDecisionConflictAsync(caseId, DeathCaseStatus.ApprovedForHandover, ct);
        if (current != null)
        {
            return current;
        }
        throw NotAllowed(DeathCaseNotAllowedReason.PlanStateInvalid);
    }

    /// <summary>
    /// DEATH-01: gọi lặp = hồ sơ đang SupplementRequired và quyết định mới nhất là yêu cầu bổ sung của chính tôi,
    /// cùng bản đang xét, cùng lý do, cùng các phần cần bổ sung.
    /// </summary>
    private static bool IsRepeatedSupplement(ReviewContext context, string reason, string requestedParts)
    {
        DeathCase deathCase = context.DeathCase;
        if (deathCase.Status != DeathCaseStatus.SupplementRequired)
        {
            return false;
        }
        DeathCaseDecision? latest = null;
        foreach (DeathCaseDecision decision in deathCase.Decisions)
        {
            if (latest == null || VerifierDeathCaseMappings.CompareDecisions(decision, latest) > 0)
            {
                latest = decision;
            }
        }
        if (latest == null || latest.Kind != DeathCaseDecisionKind.SupplementRequested)
        {
            return false;
        }
        if (latest.VerifierUserId != context.UserId || deathCase.ReviewVersionNo == null)
        {
            return false;
        }
        return latest.VersionNo == deathCase.ReviewVersionNo.Value
            && string.Equals(latest.Reason, reason, StringComparison.Ordinal)
            && string.Equals(latest.RequestedParts, requestedParts, StringComparison.Ordinal);
    }

    /// <summary>DEATH-03, STATE-03: dòng quyết định gắn bản đang xét và phân công đang hiệu lực đã tải.</summary>
    private static DeathCaseDecision NewDecision(
        ReviewContext context, DeathCaseDecisionKind kind, string reason, DateTimeOffset now)
    {
        DeathCaseDecision decision = new DeathCaseDecision();
        decision.CaseId = context.DeathCase.Id;
        decision.VersionNo = DeathCaseReviewRules.FindReviewVersion(context.DeathCase).VersionNo;
        decision.Kind = kind;
        decision.Reason = reason;
        decision.VerifierUserId = context.Assignment.VerifierUserId;
        decision.AssignmentId = context.Assignment.Id;
        decision.DecidedAt = now;
        return decision;
    }

    /// <summary>AC-71: hồ sơ đã duyệt hoặc từ chối thì không nhận thao tác ghi nào nữa → 409 CASE_CLOSED.</summary>
    private static void EnsureNotClosed(DeathCase deathCase)
    {
        if (DeathCaseReviewRules.IsClosed(deathCase.Status))
        {
            throw NotAllowed(DeathCaseNotAllowedReason.CaseClosed);
        }
    }

    /// <summary>STATE-03: etag lệch (Executor vừa gửi bản mới, hoặc thao tác khác của Verifier) → 409 EDIT_CONFLICT.</summary>
    private static void EnsureEtag(string? etag, DeathCase deathCase)
    {
        if (!Etag.Matches(etag, deathCase.UpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Hồ sơ chứng tử vừa được sửa ở nơi khác. Vui lòng tải lại rồi thao tác tiếp.");
        }
    }

    /// <summary>Bổ sung, từ chối, xem xét cờ chỉ làm được khi hồ sơ đang xét hoặc đang chờ bổ sung.</summary>
    private static void EnsureCanDecide(DeathCase deathCase)
    {
        if (!DeathCaseReviewRules.CanDecide(deathCase.Status))
        {
            throw NotAllowed(DeathCaseNotAllowedReason.NotUnderReview);
        }
    }

    private async Task EnsureNoConflictOfInterestAsync(ReviewContext context, CancellationToken ct)
    {
        bool conflict = await HasConflictOfInterestAsync(context.DeathCase, context.UserId, ct);
        if (conflict)
        {
            throw NotAllowed(DeathCaseNotAllowedReason.VerifierConflict);
        }
    }

    /// <summary>
    /// ROLE-03, AC-67: Verifier là Owner của kho, Executor đang hiệu lực của kho, người gửi bản đang xét,
    /// hoặc người nhận của kho (so email với người nhận trong gói READY, cùng hàm với lúc chọn Verifier, D1).
    /// Giới hạn: người nhận không khai email hoặc dùng email khác cho tài khoản thì chưa bắt được trước F3.
    /// </summary>
    private async Task<bool> HasConflictOfInterestAsync(DeathCase deathCase, Guid userId, CancellationToken ct)
    {
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null)
        {
            // Hồ sơ luôn gắn với một kho; thiếu kho là dữ liệu hỏng nên không cho quyết định.
            return true;
        }
        if (vault.OwnerId == userId)
        {
            return true;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(deathCase);
        if (reviewVersion.SubmittedByUserId == userId)
        {
            return true;
        }

        // ROLE-03: Verifier là người nhận của kho (so email, D1). Dùng chung hàm với lúc chọn Verifier.
        User? user = await userRepository.FindByIdAsync(userId, ct);
        if (user != null)
        {
            string userEmail = DeathCaseAssignmentRules.NormalizeEmail(user.Email);
            List<string> beneficiaryEmails = await verifierPicker.GetBeneficiaryEmailsAsync(vault.Id, ct);
            foreach (string email in beneficiaryEmails)
            {
                if (string.Equals(email, userEmail, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>SEC-07: dòng nhật ký của Verifier trên bản hồ sơ đang xét. reason chỉ nhận mã, không nhận lý do tự do.</summary>
    /// <summary>
    /// Gửi thư cho mọi Executor đang nhận việc được của kho (đã chấp nhận, tài khoản còn hoạt động), vì ai trong số đó
    /// cũng có thể nộp bản bổ sung hoặc mở hồ sơ mới. Bỏ qua tài khoản không còn hoặc không có email.
    /// </summary>
    private async Task NotifyExecutorsAsync(
        DeathCase deathCase, DeathCaseDecisionKind kind, DateTimeOffset? supplementDueAt, CancellationToken ct)
    {
        List<Executor> executors = await executorReader.GetAvailableAsync(deathCase.VaultId, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == null)
            {
                continue;
            }
            User? user = await userRepository.FindByIdAsync(executor.UserId.Value, ct);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                continue;
            }
            DeathCaseExecutorNotice notice = new DeathCaseExecutorNotice();
            notice.CaseId = deathCase.Id;
            notice.ToEmail = user.Email;
            notice.FullName = user.FullName;
            notice.CaseCode = DeathCaseRules.CaseCode(deathCase.Id);
            notice.Kind = kind;
            notice.SupplementDueAt = supplementDueAt;
            await executorNoticeSender.SendAsync(notice, ct);
        }
    }

    private void AddAudit(ReviewContext context, AuditAction action, string? reason, DateTimeOffset now)
    {
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(context.DeathCase);
        auditLogRepository.Add(AuditRules.DeathCaseAction(
            context.DeathCase.VaultId, context.UserId, AuditActorRole.Verifier, action, context.DeathCase.Id,
            reviewVersion.VersionNo, reason, now));
    }

    private static AppException CaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
    }

    private static AppException NotAllowed(DeathCaseNotAllowedReason reason)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseNotAllowed,
            "Hồ sơ chứng tử hiện không cho phép thao tác này.", extensions);
    }

    /// <summary>Hồ sơ đã nạp, phân công đang hiệu lực của tôi và Id tôi.</summary>
    private sealed class ReviewContext
    {
        public DeathCase DeathCase { get; set; } = null!;

        public DeathCaseAssignment Assignment { get; set; } = null!;

        public Guid UserId { get; set; }
    }
}
