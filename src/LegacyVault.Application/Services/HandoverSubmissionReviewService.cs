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

// Luật sư kiểm danh tính người nhận (bước 2 của biên bản bàn giao), các nút theo thứ tự trên màn luật sư:
//   1. Danh sách hồ sơ chờ kiểm tra ........ ListPendingAsync
//   2. Mở một hồ sơ ........................ GetAsync
//   3. Xem ảnh căn cước mặt trước, mặt sau .. PrepareImageAsync
//   4. Xác nhận danh tính .................. ApproveAsync (đủ người thì biên bản sang bước ký)
//   5. Yêu cầu làm lại ..................... RequestSupplementAsync
// Mọi nút đều qua LoadForVerifierAsync: chỉ luật sư được giao biên bản của kho mới thấy hồ sơ.
/// <summary>Luật sư kiểm danh tính người nhận có tên trong biên bản bàn giao.</summary>
public sealed class HandoverSubmissionReviewService : IHandoverSubmissionReviewService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IAssetContentStore contentStore;
    private readonly IAuditLogRepository auditLogs;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IHandoverRecordRepository records;
    private readonly IHandoverRecordService recordService;

    public HandoverSubmissionReviewService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IHandoverSubmissionRepository submissions,
        IAssetContentStore contentStore,
        IAuditLogRepository auditLogs,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IHandoverRecordRepository records,
        IHandoverRecordService recordService)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.submissions = submissions;
        this.contentStore = contentStore;
        this.auditLogs = auditLogs;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.records = records;
        this.recordService = recordService;
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
        // 1. Tìm hồ sơ; không thuộc luật sư này thì báo không tìm thấy
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
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

    // Bước 4: luật sư xác nhận danh tính người nhận; đủ người thì biên bản sang bước ký ba bên.
    public async Task<VerifierHandoverSubmissionResponse> ApproveAsync(Guid submissionId, CancellationToken ct)
    {
        // 1. Tìm hồ sơ và lượt của nó
        HandoverSubmission submission = await LoadForVerifierAsync(submissionId, ct);
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = submission.Turn;

        // 2. Các điều kiện: chưa duyệt, đang chờ duyệt,
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
        // Kết quả đối chiếu chỉ để tham khảo: luật sư xem ảnh căn cước và tự quyết định, kể cả khi có trường không khớp.
        await RequireIdentityStepAsync(turn, ct);
        Vault vault = await RequireVaultInHandoverAsync(turn, ct);
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }

        // 3. Đánh dấu hồ sơ đã duyệt, lượt sang chờ ký, ghi người xác nhận danh tính
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

        // Đủ người nhận đã được xác nhận thì biên bản sang bước ký.
        await recordService.AdvanceAfterIdentityAsync(turn, now, ct);

        // 4. Ghi nhật ký rồi lưu một lần
        auditLogs.Add(AuditRules.Create(
            turn.VaultId, userId, AuditActorRole.Verifier, AuditAction.HandoverSubmissionApproved,
            AuditTargetType.HandoverTurn, turn.Id, null, AuditOutcome.Succeeded, null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return submission.ToVerifierDetail();
    }

    // Bước 5: luật sư yêu cầu người nhận làm lại xác minh danh tính, kèm lý do.
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

        // 3. Đánh dấu hồ sơ cần làm lại: người nhận nộp lại ảnh căn cước; chưa khớp thì chưa sang bước ký
        DateTimeOffset now = timeProvider.GetUtcNow();
        submission.Status = HandoverSubmissionStatus.SupplementRequested;
        submission.SupplementReason = reason;
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
                "Chỉ luật sư mới kiểm được danh tính người nhận.");
        }
        return userId;
    }

    /// <summary>Luật sư hợp lệ và được giao biên bản của kho; không thì 404 như hồ sơ không tồn tại.</summary>
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

    /// <summary>Biên bản của lượt phải đang ở bước kiểm danh tính (đã chốt, chưa mở lại để sửa).</summary>
    private async Task RequireIdentityStepAsync(HandoverTurn turn, CancellationToken ct)
    {
        if (turn.RecordId == null)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không thuộc biên bản bàn giao nào.");
        }
        HandoverRecord? record = await records.FindByIdAsync(turn.RecordId.Value, ct);
        if (record == null || record.Status != HandoverRecordStatus.IdentityCheck)
        {
            throw HandoverAccess.NotAllowed("NOT_IDENTITY_STEP",
                "Biên bản chưa ở bước kiểm danh tính. Hãy chốt biên bản trước.");
        }
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
        return vault;
    }

    /// <summary>Lỗi 404 dùng chung cho hồ sơ không có, hồ sơ của kho khác và hồ sơ Người xét duyệt không được xem.</summary>
    private static AppException SubmissionNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy hồ sơ người nhận.");
    }
}
