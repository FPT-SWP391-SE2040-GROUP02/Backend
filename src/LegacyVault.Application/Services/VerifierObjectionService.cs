using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// ALIVE-05..07: kết luận và mọi hệ quả (hồ sơ, lượt, grant, kế hoạch, kỳ điểm danh) lưu trong MỘT giao dịch.
/// Bác phản đối KHÔNG tự duyệt chứng tử: hồ sơ chỉ trở về trạng thái trước đó.
/// TODO(LV-07): thông báo người nhận có grant bị thu hồi (ALIVE-07), nhắc Verifier quá hạn và giao người khác (ALIVE-08).
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
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public VerifierObjectionService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IDeathCaseRepository deathCases,
        IExecutorReader executorReader,
        IObjectionRepository objections,
        ICheckInRepository checkIns,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.deathCases = deathCases;
        this.executorReader = executorReader;
        this.objections = objections;
        this.checkIns = checkIns;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ObjectionResponse> GetAsync(Guid caseId, CancellationToken ct)
    {
        Context context = await LoadContextAsync(caseId, ct);
        DeathCaseObjection? objection = await objections.FindLatestByCaseAsync(caseId, ct);
        if (objection == null)
        {
            throw ObjectionMapper.CaseNotFound();
        }
        return ObjectionMapper.ToResponse(objection, timeProvider.GetUtcNow());
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
            // Giữ chặn; ghi lý do và hạn mới cho Verifier (không đặt lại hạn selfie của Owner).
            objection.InsufficientEvidenceCount = objection.InsufficientEvidenceCount + 1;
            objection.ConclusionReason = reason;
            objection.VerifierDueAt = now + ObjectionRules.VerifierWindow;
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
            await ApplyObjectionRejectedAsync(context, objection, now, ct);
        }

        objection.Status = ObjectionStatus.Concluded;
        objection.Conclusion = conclusion;
        objection.ConclusionReason = reason;
        objection.ConcludedByUserId = context.UserId;
        objection.ConcludedAt = now;
        objection.UpdatedAt = now;
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
                assignment.EndReason = "ALIVE_CONFIRMED";
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
    private async Task<Context> LoadContextAsync(Guid caseId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Verifier || user.Status != UserStatus.Active)
        {
            throw ObjectionMapper.CaseNotFound();
        }

        DeathCase? deathCase = await deathCases.FindByIdAsync(caseId, ct);
        if (deathCase == null || !IsAssignedTo(deathCase, userId))
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
