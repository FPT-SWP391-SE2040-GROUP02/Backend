using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Thư nhắc/báo sự cố cho nhân viên (ASSIGN-04, bảng thời hạn: nhắc sau 48 giờ, báo Admin sau 72 giờ).</summary>
public enum IncidentNoticeKind
{
    Assigned = 0,
    Reminder = 1,
    Escalation = 2,
}

public sealed class IncidentNotice
{
    public IncidentNoticeKind Kind { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public Guid IncidentId { get; set; }

    public string IncidentType { get; set; } = string.Empty;

    public DateTimeOffset DueAt { get; set; }
}

/// <summary>Gửi thư sự cố. Gửi lỗi thì ném exception; người gọi không đánh dấu đã nhắc để vòng sau thử lại.</summary>
public interface IIncidentNoticeSender
{
    Task SendAsync(IncidentNotice notice, CancellationToken ct);
}

/// <summary>Ghi nhận các tình huống "bị kẹt" từ job nền vào sự cố có người phụ trách (SRS §6.4, ASSIGN-04/05, DMS-05).</summary>
public interface IOperationsRecorder
{
    /// <summary>
    /// Sau mỗi lần job phân công xử lý hồ sơ: không có Verifier phù hợp (NoCandidate) → mở sự cố NO_VERIFIER;
    /// đã giao được (Assigned/Reassigned) → đóng sự cố đó. Lưu ngay.
    /// </summary>
    Task RecordAssignmentOutcomeAsync(Guid caseId, DeathCaseAssignmentOutcome outcome, CancellationToken ct);

    /// <summary>
    /// Sau mỗi lần job điểm danh xử lý kỳ: kế hoạch sang AWAITING_CHECK → mở sự cố CHECK_IN_OVERDUE giao cho Executor chính
    /// đang hiệu lực (rồi dự phòng); không có ai → sự cố NO_EXECUTOR "bị kẹt" có người phụ trách là Admin và ngày xem lại (DMS-05).
    /// Kỳ đã đóng (Owner điểm danh) → đóng sự cố. Lưu ngay.
    /// </summary>
    Task RecordCheckInPeriodAsync(Guid periodId, CancellationToken ct);
}

public sealed class OperationsRecorder : IOperationsRecorder
{
    public const string TargetCheckInPeriod = "CHECKIN_PERIOD";

    private readonly IDeathCaseRepository deathCases;
    private readonly ICheckInRepository checkIns;
    private readonly IExecutorReader executorReader;
    private readonly IIncidentService incidentService;
    private readonly IUnitOfWork unitOfWork;

    public OperationsRecorder(
        IDeathCaseRepository deathCases,
        ICheckInRepository checkIns,
        IExecutorReader executorReader,
        IIncidentService incidentService,
        IUnitOfWork unitOfWork)
    {
        this.deathCases = deathCases;
        this.checkIns = checkIns;
        this.executorReader = executorReader;
        this.incidentService = incidentService;
        this.unitOfWork = unitOfWork;
    }

    public async Task RecordAssignmentOutcomeAsync(Guid caseId, DeathCaseAssignmentOutcome outcome, CancellationToken ct)
    {
        if (outcome == DeathCaseAssignmentOutcome.NoCandidate)
        {
            DeathCase? deathCase = await deathCases.FindByIdAsync(caseId, ct);
            if (deathCase == null)
            {
                return;
            }
            await incidentService.RaiseAsync(IncidentType.NoVerifier, deathCase.VaultId, IncidentRules.TargetDeathCase,
                deathCase.Id, "Hồ sơ chứng tử chưa có Verifier phù hợp để xét.", null, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        else if (outcome == DeathCaseAssignmentOutcome.Assigned || outcome == DeathCaseAssignmentOutcome.Reassigned)
        {
            await incidentService.ResolveByTargetAsync(
                IncidentType.NoVerifier, IncidentRules.TargetDeathCase, caseId, "Đã giao Verifier.", ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public async Task RecordCheckInPeriodAsync(Guid periodId, CancellationToken ct)
    {
        CheckInPeriod? period = await checkIns.FindPeriodForJobAsync(periodId, ct);
        if (period == null)
        {
            return;
        }
        HandoverPlan plan = period.Plan;

        bool waiting = period.ClosedAt == null && plan.Status == PlanStatus.AwaitingCheck;
        if (!waiting)
        {
            if (period.ClosedAt != null)
            {
                await incidentService.ResolveByTargetAsync(IncidentType.CheckInOverdue, TargetCheckInPeriod, period.Id,
                    "Kỳ điểm danh đã đóng.", ct);
                await incidentService.ResolveByTargetAsync(IncidentType.NoExecutor, TargetCheckInPeriod, period.Id,
                    "Kỳ điểm danh đã đóng.", ct);
                await unitOfWork.SaveChangesAsync(ct);
            }
            return;
        }

        // ASSIGN-02: Executor chính bị khóa thì sự cố giao cho Executor dự phòng còn hoạt động.
        List<Executor> active = await executorReader.GetAvailableAsync(plan.VaultId, ct);
        Guid? assignee = null;
        if (active.Count > 0)
        {
            assignee = active[0].UserId;
        }
        if (assignee != null)
        {
            await incidentService.RaiseAsync(IncidentType.CheckInOverdue, plan.VaultId, TargetCheckInPeriod, period.Id,
                "Owner quá hạn điểm danh; Executor kiểm tra tình trạng Owner.", assignee, ct);
        }
        else
        {
            // ASSIGN-05: không còn Executor thay thế → ghi "bị kẹt" (Admin phụ trách, có ngày xem lại), không mở kho.
            await incidentService.RaiseAsync(IncidentType.NoExecutor, plan.VaultId, TargetCheckInPeriod, period.Id,
                "Owner quá hạn điểm danh nhưng chưa có Executor hiệu lực.", null, ct);
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}
