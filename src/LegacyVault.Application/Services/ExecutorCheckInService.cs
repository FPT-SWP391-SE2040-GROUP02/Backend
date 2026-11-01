using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// Cùng hiệu ứng với Owner điểm danh (đóng kỳ cũ, mở kỳ mới, kế hoạch về ACTIVE), nhưng chỉ cho phép khi
/// Owner đang quá hạn và kho chưa có hồ sơ chứng tử đang mở. Dòng nhật ký ghi vai trò Executor.
/// </remarks>
public sealed class ExecutorCheckInService : IExecutorCheckInService
{
    private const string ReasonPlanNotOverdue = "PLAN_NOT_OVERDUE";
    private const string ReasonDeathCaseOpen = "DEATH_CASE_OPEN";
    private const string AuditReasonProxy = "proxyByExecutor";

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorReader executorReader;
    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly ICheckInRepository checkInRepository;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ExecutorCheckInService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorReader executorReader,
        IDeathCaseRepository deathCaseRepository,
        ICheckInRepository checkInRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.executorReader = executorReader;
        this.deathCaseRepository = deathCaseRepository;
        this.checkInRepository = checkInRepository;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ExecutorCheckInResponse> CheckInForOwnerAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        HandoverPlan plan = vault.Plan;
        DeathCase? openCase = await deathCaseRepository.FindOpenByVaultIdAsync(vault.Id, ct);
        if (openCase != null)
        {
            throw NotAllowed(ReasonDeathCaseOpen, plan.Status);
        }
        if (plan.Status != PlanStatus.CheckinOverdue && plan.Status != PlanStatus.AwaitingCheck)
        {
            throw NotAllowed(ReasonPlanNotOverdue, plan.Status);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        CheckInSchedule? schedule = await checkInRepository.FindScheduleByPlanIdAsync(plan.Id, ct);
        if (schedule == null)
        {
            schedule = new CheckInSchedule();
            schedule.PlanId = plan.Id;
            schedule.CreatedAt = now;
            checkInRepository.AddSchedule(schedule);
        }

        CheckInPeriod? openPeriod = await checkInRepository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (openPeriod != null)
        {
            openPeriod.CheckedInAt = now;
            openPeriod.ClosedAt = now;
        }

        CheckInRules.ApplyPendingSettings(schedule, now);
        schedule.LastCheckInAt = now;

        int lastSequence = await checkInRepository.GetMaxSequenceAsync(plan.Id, ct);
        CheckInPeriod next = new CheckInPeriod();
        next.PlanId = plan.Id;
        next.Sequence = lastSequence + 1;
        next.StartedAt = now;
        next.DueAt = CheckInTimeline.ComputeDueAt(now, schedule.CycleDays);
        next.GraceEndsAt = CheckInTimeline.ComputeGraceEndsAt(next.DueAt, schedule.GraceDays);
        checkInRepository.AddPeriod(next);

        plan.Status = PlanStatus.Active;
        plan.UpdatedAt = now;

        // SEC-13: TargetId là kỳ mới, TargetVersion là số thứ tự kỳ; reason chỉ là mã, Owner xem được nhật ký.
        auditLogRepository.Add(AuditRules.Create(
            vault.Id, userId, AuditActorRole.Executor, AuditAction.CheckedIn, AuditTargetType.CheckIn,
            next.Id, next.Sequence, AuditOutcome.Succeeded, AuditReasonProxy, now));

        // Một lần lưu (STATE-02). Hai yêu cầu song song: RowVersion của kỳ cũ lệch nên bên thua nhận 409 EDIT_CONFLICT.
        await unitOfWork.SaveChangesAsync(ct);

        ExecutorCheckInResponse response = new ExecutorCheckInResponse();
        response.VaultId = vault.Id;
        response.PlanStatus = plan.Status.ToApiString();
        response.CheckedInAt = now;
        response.NextCheckInDueAt = next.DueAt;
        response.GraceEndsAt = next.GraceEndsAt;
        return response;
    }

    /// <summary>ROLE-03: Owner không bao giờ là Executor của chính kho; chỉ Executor Accepted mới tính.</summary>
    private async Task<bool> IsActiveExecutorAsync(Vault vault, Guid userId, CancellationToken ct)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    private static AppException NotAllowed(string reason, PlanStatus planStatus)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        extensions["planStatus"] = planStatus.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.CheckinNotAllowed,
            "Hiện chưa thể điểm danh thay chủ kho.", extensions);
    }
}
