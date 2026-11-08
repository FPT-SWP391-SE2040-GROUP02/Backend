using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class ExecutorCheckInService : IExecutorCheckInService
{
    private const string ReasonPlanNotOverdue = "PLAN_NOT_OVERDUE";
    private const string ReasonDeathCaseOpen = "DEATH_CASE_OPEN";
    private const string ReasonProxyLimitReached = "PROXY_LIMIT_REACHED";
    private const string AuditReasonPrefix = "Executor điểm danh thay chủ kho. Cách liên lạc: ";

    /// <summary>Cho phép đồng hồ máy Executor lệch nhẹ so với server khi khai lúc liên lạc.</summary>
    private static readonly TimeSpan ContactedAtTolerance = TimeSpan.FromMinutes(5);

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

    public async Task<ExecutorCheckInResponse> CheckInForOwnerAsync(
        Guid vaultId, ExecutorCheckInRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        HandoverPlan plan = vault.Plan;
        if (!IsOverdue(plan.Status))
        {
            throw NotAllowed(plan.Status, ReasonPlanNotOverdue);
        }

        // Đã có hồ sơ chứng tử đang mở (kể cả bản nháp) nghĩa là đã có giấy chứng tử: không điểm danh hộ nữa.
        DeathCase? openCase = await deathCaseRepository.FindOpenByVaultIdAsync(vault.Id, ct);
        if (openCase != null)
        {
            throw NotAllowed(plan.Status, ReasonDeathCaseOpen);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (request.ContactedAt!.Value > now.Add(ContactedAtTolerance))
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                "Lúc liên lạc không được ở tương lai.");
        }

        // Giới hạn số lần điểm danh thay liên tiếp; chủ kho tự điểm danh thì bộ đếm về 0.
        int usedBefore = await checkInRepository.CountConsecutiveExecutorPeriodsAsync(plan.Id, ct);
        if (usedBefore >= ProxyCheckInRules.Limit())
        {
            throw NotAllowed(plan.Status, ReasonProxyLimitReached);
        }

        CheckInSchedule? schedule = await checkInRepository.FindScheduleByPlanIdAsync(plan.Id, ct);
        if (schedule == null)
        {
            schedule = new CheckInSchedule();
            schedule.PlanId = plan.Id;
            schedule.CreatedAt = now;
            checkInRepository.AddSchedule(schedule);
        }

        // Kỳ quá hạn đóng lại nhưng không ghi CheckedInAt, vì chủ kho không tự điểm danh.
        // Sự cố quá hạn của kỳ này được job sự cố tự đóng khi thấy kỳ đã đóng.
        CheckInPeriod? openPeriod = await checkInRepository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (openPeriod != null)
        {
            openPeriod.ClosedAt = now;
        }

        // Không cập nhật LastCheckInAt: giờ điểm danh gần nhất chỉ dành cho lượt chủ kho tự điểm danh.
        CheckInRules.ApplyPendingSettings(schedule, now);

        int lastSequence = await checkInRepository.GetMaxSequenceAsync(plan.Id, ct);
        CheckInPeriod next = new CheckInPeriod();
        next.PlanId = plan.Id;
        next.Sequence = lastSequence + 1;
        next.StartedAt = now;
        next.StartedByExecutor = true;
        next.ProxyContactedWho = request.ContactedWho.Trim();
        next.ProxyContactedAt = request.ContactedAt.Value;
        next.ProxyContactMethod = request.ContactMethod;
        next.DueAt = CheckInTimeline.ComputeDueAt(now, schedule.CycleDays);
        next.GraceEndsAt = CheckInTimeline.ComputeGraceEndsAt(next.DueAt, schedule.GraceDays);
        checkInRepository.AddPeriod(next);

        plan.Status = PlanStatus.Active;
        plan.UpdatedAt = now;

        auditLogRepository.Add(AuditRules.Create(
            vault.Id, userId, AuditActorRole.Executor, AuditAction.CheckedIn, AuditTargetType.CheckIn,
            next.Id, next.Sequence, AuditOutcome.Succeeded, AuditReasonPrefix + request.ContactMethod, now));

        // Một lần lưu cho kỳ cũ, kỳ mới, lịch và kế hoạch. Hai người cùng điểm danh: RowVersion của kỳ cũ lệch
        // nên bên thua nhận 409 EDIT_CONFLICT.
        await unitOfWork.SaveChangesAsync(ct);

        ExecutorCheckInResponse response = new ExecutorCheckInResponse();
        response.VaultId = vault.Id;
        response.PlanStatus = plan.Status.ToApiString();
        response.CheckedInAt = now;
        response.NextCheckInDueAt = next.DueAt;
        response.GraceEndsAt = next.GraceEndsAt;
        response.ProxyCheckInLimit = ProxyCheckInRules.Limit();
        response.ProxyCheckInsLeft = ProxyCheckInRules.Left(usedBefore + 1);
        return response;
    }

    /// <summary>Chủ kho đã quá hạn: đang trong thời gian chờ hoặc đã hết thời gian chờ.</summary>
    private static bool IsOverdue(PlanStatus status)
    {
        if (status == PlanStatus.CheckinOverdue || status == PlanStatus.AwaitingCheck)
        {
            return true;
        }
        return false;
    }

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

    private static AppException NotAllowed(PlanStatus planStatus, string reason)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["planStatus"] = planStatus.ToApiString();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.CheckinNotAllowed,
            "Hiện không thể điểm danh thay chủ kho.", extensions);
    }
}
