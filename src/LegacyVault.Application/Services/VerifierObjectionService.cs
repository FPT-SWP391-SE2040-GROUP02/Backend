using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// ALIVE-05..07: kết luận và mọi hệ quả (hồ sơ, lượt, grant, kế hoạch, kỳ điểm danh) lưu trong MỘT giao dịch.
/// Bác phản đối KHÔNG tự duyệt chứng tử: hồ sơ chỉ trở về trạng thái trước đó.
/// ALIVE-08: DeathCaseAssignmentJob giao lại Verifier quá hạn/mất quyền.
/// TODO(LV-07): thông báo người nhận có grant bị thu hồi (ALIVE-07).
/// </remarks>
public sealed class VerifierObjectionService : IVerifierObjectionService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IDeathCaseRepository deathCases;
    private readonly IExecutorReader executorReader;
    private readonly IObjectionRepository objections;
    private readonly ICheckInRepository checkIns;
    private readonly IHoldService holdService;
    private readonly IIncidentService incidentService;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IAssetContentStore contentStore;

    public VerifierObjectionService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IDeathCaseRepository deathCases,
        IExecutorReader executorReader,
        IObjectionRepository objections,
        ICheckInRepository checkIns,
        IHoldService holdService,
        IIncidentService incidentService,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IAssetContentStore contentStore)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.deathCases = deathCases;
        this.executorReader = executorReader;
        this.objections = objections;
        this.checkIns = checkIns;
        this.holdService = holdService;
        this.incidentService = incidentService;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.contentStore = contentStore;
    }

    public async Task<ObjectionResponse> GetAsync(Guid caseId, CancellationToken ct)
    {
        Context context = await LoadContextAsync(caseId, ct, true);
        DeathCaseObjection? objection = await objections.FindLatestByCaseAsync(caseId, ct);
        if (objection == null)
        {
            throw ObjectionMapper.CaseNotFound();
        }
        return ObjectionMapper.ToResponse(objection, timeProvider.GetUtcNow());
    }

    public async Task<PreparedDownload> PrepareSelfieImageAsync(Guid caseId, CancellationToken ct)
    {
        await LoadContextAsync(caseId, ct, true);
        DeathCaseObjection? objection = await objections.FindLatestByCaseAsync(caseId, ct);
        if (objection == null)
        {
            throw ObjectionMapper.CaseNotFound();
        }
        if (objection.SelfieImageStorageKey == null || objection.SelfieImageWrappedDataKey == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.IdentityImageNotFound,
                "Chủ kho chưa gửi ảnh selfie nào được so khớp.");
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = objection.SelfieImageStorageKey;
        stored.SizeBytes = objection.SelfieImageSizeBytes ?? 0;
        stored.EncryptionKeyId = objection.SelfieImageEncryptionKeyId ?? string.Empty;
        stored.WrappedDataKey = objection.SelfieImageWrappedDataKey;

        string contentType = objection.SelfieImageContentType ?? "application/octet-stream";
        return await IdentityImageStorage.PrepareAsync(contentStore, stored, contentType, "selfie", ct);
    }

    public async Task<ObjectionResponse> ConcludeAsync(Guid caseId, ConcludeObjectionRequest request, CancellationToken ct)
    {
        ObjectionConclusion conclusion = ParseConclusion(request.Conclusion);
        string reason = request.Reason.Trim();
        if (reason.Length == 0)
        {
            throw ValidationErrors.For("reason", "Vui lòng ghi lý do.");
        }
        if (reason.Length > ObjectionRules.MaxReasonLength)
        {
            throw ValidationErrors.For("reason", "Lý do tối đa 1000 ký tự.");
        }

        Context context = await LoadContextAsync(caseId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCaseObjection? objection = await objections.FindOpenByCaseAsync(caseId, ct);
        if (objection == null)
        {
            // Gọi lặp cùng kết luận trên báo đã đóng: trả trạng thái hiện tại.
            DeathCaseObjection? closed = await objections.FindLatestByCaseAsync(caseId, ct);
            if (closed != null && closed.Conclusion == conclusion && closed.ConcludedByUserId == context.UserId)
            {
                return ObjectionMapper.ToResponse(closed, now);
            }
            throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.ObjectionNotOpen);
        }

        if (conclusion == ObjectionConclusion.InsufficientEvidence)
        {
            // Giữ chặn và yêu cầu Owner chụp selfie mới (BR-76 c): selfie cũ không còn tính, mở 7 ngày chụp mới,
            // hạn Verifier và ngày xem lại hold tính lại theo hạn selfie mới. Owner thấy lý do qua conclusionReason.
            objection.InsufficientEvidenceCount = objection.InsufficientEvidenceCount + 1;
            objection.ConclusionReason = reason;
            ObjectionRules.RequestNewSelfie(objection, now);
            objection.VerifierDueAt = ObjectionRules.ComputeVerifierDueAt(now, objection.SelfieDeadlineAt);
            await holdService.RescheduleOwnerAliveAsync(context.Vault.Id, objection.SelfieDeadlineAt, ct);
            objection.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
            return ObjectionMapper.ToResponse(objection, now);
        }

        if (conclusion == ObjectionConclusion.AliveConfirmed)
        {
            if (!ObjectionRules.HasAliveEvidence(objection, now))
            {
                throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.AliveEvidenceRequired);
            }
            await ApplyAliveConfirmedAsync(context, objection, now, ct);
        }
        else
        {
            // ALIVE-05: selfie đã khớp thì không bác được; kiểm tra trước mọi thay đổi entity.
            if (!ObjectionRules.CanReject(objection))
            {
                throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.OwnerSelfiePassed);
            }
            await ApplyObjectionRejectedAsync(context, objection, now, ct);
        }

        objection.Status = ObjectionStatus.Concluded;
        objection.Conclusion = conclusion;
        objection.ConclusionReason = reason;
        objection.ConcludedByUserId = context.UserId;
        objection.ConcludedAt = now;
        objection.UpdatedAt = now;

        // Kết luận xong: gỡ hold chung, đóng sự cố theo dõi và ghi nhật ký trong cùng giao dịch (ALIVE-05, ALIVE-06).
        await holdService.ReleaseOwnerAliveAsync(context.Vault.Id, context.UserId, reason, ct);
        await incidentService.ResolveByTargetAsync(
            IncidentType.OwnerAlive, IncidentRules.TargetDeathCase, context.DeathCase.Id, "Verifier đã kết luận.", ct);
        audit.Add(AuditRules.Create(context.Vault.Id, context.UserId, AuditActorRole.Verifier,
            AuditAction.ObjectionConcluded, AuditTargetType.DeathCase, context.DeathCase.Id, null,
            AuditOutcome.Succeeded, conclusion.ToString().ToUpperInvariant(), now));
        await unitOfWork.SaveChangesAsync(ct);
        return ObjectionMapper.ToResponse(objection, now);
    }

    /// <summary>ALIVE-06, ALIVE-07: hồ sơ bị từ chối, lượt chưa xong bị hủy, grant bị thu hồi, kế hoạch về ACTIVE với kỳ mới.</summary>
    private async Task ApplyAliveConfirmedAsync(
        Context context, DeathCaseObjection objection, DateTimeOffset now, CancellationToken ct)
    {
        DeathCase deathCase = context.DeathCase;
        HandoverPlan plan = context.Vault.Plan;

        deathCase.Status = DeathCaseStatus.Rejected;
        deathCase.ClosedAt = now;
        deathCase.UpdatedAt = now;
        foreach (DeathCaseAssignment assignment in deathCase.Assignments)
        {
            if (assignment.EndedAt == null)
            {
                assignment.EndedAt = now;
                assignment.EndReason = DeathCaseAssignmentRules.EndReasonAliveConfirmed;
            }
        }

        List<HandoverTurn> turns = await objections.FindTurnsWithGrantsAsync(plan.Id, ct);
        foreach (HandoverTurn turn in turns)
        {
            if (!ObjectionRules.IsTurnFinished(turn.Status))
            {
                turn.Status = HandoverTurnStatus.Cancelled;
                turn.EndedAt = now;
                turn.UpdatedAt = now;
            }
            if (turn.Grant != null
                && (turn.Grant.Status == HandoverGrantStatus.Active || turn.Grant.Status == HandoverGrantStatus.Blocked))
            {
                turn.Grant.Status = HandoverGrantStatus.Revoked;
                turn.Grant.EndedAt = now;
            }
        }

        // Kế hoạch về ACTIVE và đặt lại kỳ điểm danh (ALIVE-06); grant đã hết hạn không sống lại.
        plan.Status = PlanStatus.Active;
        plan.UpdatedAt = now;
        CheckInPeriod? open = await checkIns.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (open != null)
        {
            open.ClosedAt = now;
        }
        CheckInSchedule? schedule = await checkIns.FindScheduleByPlanIdAsync(plan.Id, ct);
        int cycleDays = CheckInRules.DefaultCycleDays;
        int graceDays = CheckInRules.DefaultGraceDays;
        if (schedule != null)
        {
            cycleDays = schedule.CycleDays;
            graceDays = schedule.GraceDays;
        }
        int lastSequence = await checkIns.GetMaxSequenceAsync(plan.Id, ct);
        CheckInPeriod next = new CheckInPeriod();
        next.PlanId = plan.Id;
        next.Sequence = lastSequence + 1;
        next.StartedAt = now;
        next.DueAt = CheckInTimeline.ComputeDueAt(now, cycleDays);
        next.GraceEndsAt = CheckInTimeline.ComputeGraceEndsAt(next.DueAt, graceDays);
        checkIns.AddPeriod(next);
    }

    /// <summary>Phản đối không đúng: trả hồ sơ, kế hoạch và grant về trạng thái trước; không tự duyệt chứng tử.</summary>
    private async Task ApplyObjectionRejectedAsync(
        Context context, DeathCaseObjection objection, DateTimeOffset now, CancellationToken ct)
    {
        DeathCase deathCase = context.DeathCase;
        HandoverPlan plan = context.Vault.Plan;

        deathCase.Status = objection.PreviousCaseStatus;
        deathCase.UpdatedAt = now;
        plan.Status = objection.PreviousPlanStatus;
        plan.UpdatedAt = now;

        List<HandoverTurn> turns = await objections.FindTurnsWithGrantsAsync(plan.Id, ct);
        foreach (HandoverTurn turn in turns)
        {
            if (turn.Grant != null && turn.Grant.Status == HandoverGrantStatus.Blocked)
            {
                turn.Grant.Status = HandoverGrantStatus.Active;
            }
        }
    }

    private static ObjectionConclusion ParseConclusion(string? value)
    {
        foreach (ObjectionConclusion candidate in Enum.GetValues<ObjectionConclusion>())
        {
            if (string.Equals(value?.Trim(), EnumTextOf(candidate), StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        throw ValidationErrors.For("conclusion",
            "Kết luận chỉ nhận ALIVE_CONFIRMED, OBJECTION_REJECTED hoặc INSUFFICIENT_EVIDENCE.");
    }

    private static string EnumTextOf(ObjectionConclusion conclusion)
    {
        return LegacyVault.Application.Mappings.EnumText.ToUpperSnake(conclusion.ToString());
    }

    /// <summary>Verifier đang hoạt động, được giao hồ sơ và không phải Owner/Executor của kho (ROLE-03).</summary>
    private async Task<Context> LoadContextAsync(Guid caseId, CancellationToken ct, bool forRead = false)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Verifier || user.Status != UserStatus.Active)
        {
            throw ObjectionMapper.CaseNotFound();
        }

        DeathCase? deathCase = await deathCases.FindByIdAsync(caseId, ct);
        if (deathCase == null)
        {
            throw ObjectionMapper.CaseNotFound();
        }
        bool assigned = IsAssignedTo(deathCase, userId);
        if (!assigned && forRead && deathCase.Status == DeathCaseStatus.Rejected)
        {
            // FindByIdAsync chỉ nạp phân công còn hiệu lực: xem lại hồ sơ mà chính tôi đã kết luận "còn sống".
            DeathCaseAssignment? concluded = await deathCases.FindAliveConfirmedAssignmentAsync(caseId, userId, ct);
            assigned = concluded != null && DeathCaseAssignmentRules.CanRead(concluded, userId, deathCase.Status);
        }
        if (!assigned)
        {
            throw ObjectionMapper.CaseNotFound();
        }

        Vault? vault = await vaults.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null || vault.OwnerId == userId)
        {
            throw ObjectionMapper.CaseNotFound();
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                throw ObjectionMapper.CaseNotFound();
            }
        }

        Context context = new Context();
        context.DeathCase = deathCase;
        context.Vault = vault;
        context.UserId = userId;
        return context;
    }

    private static bool IsAssignedTo(DeathCase deathCase, Guid userId)
    {
        foreach (DeathCaseAssignment assignment in deathCase.Assignments)
        {
            if (assignment.EndedAt == null && assignment.VerifierUserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    private sealed class Context
    {
        public DeathCase DeathCase { get; set; } = null!;

        public Vault Vault { get; set; } = null!;

        public Guid UserId { get; set; }
    }
}
