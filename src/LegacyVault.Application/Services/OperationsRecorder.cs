using LegacyVault.Application.Mappings;
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

/// <summary>Khu vực web của người nhận thư; quyết định link trong thư (Executor và Verifier không vào được /admin).</summary>
public enum IncidentRecipientArea
{
    Admin = 0,
    Verifier = 1,
    Executor = 2,
}

public sealed class IncidentNotice
{
    public IncidentNoticeKind Kind { get; set; }

    public IncidentRecipientArea Area { get; set; }

    /// <summary>Kho của sự cố; link cho Executor mở đúng kho.</summary>
    public Guid? VaultId { get; set; }

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
    /// Sau mỗi lần job điểm danh xử lý kỳ: kế hoạch sang AWAITING_CHECK → mở sự cố CHECK_IN_OVERDUE giao cho Executor
    /// đang hiệu lực; không có ai → sự cố NO_EXECUTOR "bị kẹt" có người phụ trách là Admin và ngày xem lại (DMS-05).
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
    private readonly IIncidentRepository incidents;
    private readonly IUserRepository users;
    private readonly IIncidentNoticeSender noticeSender;
    private readonly IUnitOfWork unitOfWork;

    public OperationsRecorder(
        IDeathCaseRepository deathCases,
        ICheckInRepository checkIns,
        IExecutorReader executorReader,
        IIncidentService incidentService,
        IIncidentRepository incidents,
        IUserRepository users,
        IIncidentNoticeSender noticeSender,
        IUnitOfWork unitOfWork)
    {
        this.deathCases = deathCases;
        this.checkIns = checkIns;
        this.executorReader = executorReader;
        this.incidentService = incidentService;
        this.incidents = incidents;
        this.users = users;
        this.noticeSender = noticeSender;
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

        // ASSIGN-02: chỉ giao cho Executor đang hoạt động; bị khóa thì sự cố sang nhánh không có người nhận việc.
        List<Executor> active = await executorReader.GetAvailableAsync(plan.VaultId, ct);
        Guid? assignee = null;
        if (active.Count > 0)
        {
            assignee = active[0].UserId;
        }
        if (assignee != null)
        {
            // Job gọi lại hàm này mỗi vòng; chỉ báo Executor ở lần đầu mở sự cố, các lần sau RaiseAsync trả sự cố cũ.
            Incident? alreadyOpen = await incidents.FindOpenAsync(
                IncidentType.CheckInOverdue, TargetCheckInPeriod, period.Id, ct);
            Incident incident = await incidentService.RaiseAsync(IncidentType.CheckInOverdue, plan.VaultId,
                TargetCheckInPeriod, period.Id, "Owner quá hạn điểm danh; Executor kiểm tra tình trạng Owner.", assignee, ct);
            await unitOfWork.SaveChangesAsync(ct);
            if (alreadyOpen == null)
            {
                await NotifyExecutorAsync(incident, assignee.Value, ct);
            }
            return;
        }

        // ASSIGN-05: không còn Executor thay thế → ghi "bị kẹt" (Admin phụ trách, có ngày xem lại), không mở kho.
        await incidentService.RaiseAsync(IncidentType.NoExecutor, plan.VaultId, TargetCheckInPeriod, period.Id,
            "Owner quá hạn điểm danh nhưng chưa có Executor hiệu lực.", null, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// DMS-05: kế hoạch vừa sang AWAITING_CHECK thì báo ngay Executor được giao, link về trang Executor của kho.
    /// Gửi sau khi đã lưu sự cố; gửi lỗi không làm hỏng việc đã ghi, thư nhắc sau 48 giờ của IncidentJob là lưới an toàn.
    /// </summary>
    private async Task NotifyExecutorAsync(Incident incident, Guid executorUserId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(executorUserId, ct);
        if (user == null || string.IsNullOrWhiteSpace(user.Email) || user.Status != UserStatus.Active)
        {
            return;
        }
        IncidentNotice notice = new IncidentNotice();
        notice.Kind = IncidentNoticeKind.Assigned;
        notice.Area = IncidentRecipientArea.Executor;
        notice.ToEmail = user.Email;
        notice.FullName = user.FullName;
        notice.IncidentId = incident.Id;
        notice.IncidentType = incident.Type.ToApiString();
        notice.VaultId = incident.VaultId;
        notice.DueAt = incident.DueAt;
        try
        {
            await noticeSender.SendAsync(notice, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Không ném lại: sự cố đã lưu, IncidentJob sẽ nhắc Executor khi tới hạn.
        }
    }
}
