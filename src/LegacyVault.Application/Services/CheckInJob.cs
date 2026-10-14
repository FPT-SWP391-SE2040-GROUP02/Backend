using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class CheckInJob : ICheckInJob
{
    private readonly ICheckInRepository repository;
    private readonly ICheckInReminderSender reminderSender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public CheckInJob(
        ICheckInRepository repository,
        ICheckInReminderSender reminderSender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.repository = repository;
        this.reminderSender = reminderSender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(CancellationToken ct)
    {
        return repository.FindOpenPeriodIdsForJobAsync(timeProvider.GetUtcNow(), ct);
    }

    public async Task ProcessPeriodAsync(Guid periodId, CancellationToken ct)
    {
        CheckInPeriod? period = await repository.FindPeriodForJobAsync(periodId, ct);
        if (period == null || period.ClosedAt != null)
        {
            return;
        }

        HandoverPlan plan = period.Plan;
        if (plan.Status != PlanStatus.Active && plan.Status != PlanStatus.CheckinOverdue)
        {
            // DMS-03: kế hoạch nháp, đã khóa hoặc đang xử lý chứng tử thì job không đụng tới.
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (MoveToNextStatus(period, now))
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        if (plan.Status == PlanStatus.AwaitingCheck)
        {
            // Đã giao việc cho Executor; không nhắc Owner nữa.
            return;
        }
        await SendLatestReminderAsync(period, now, ct);
    }

    /// <summary>
    /// ACTIVE → CHECKIN_OVERDUE khi tới hạn, CHECKIN_OVERDUE → AWAITING_CHECK khi hết thời gian chờ.
    /// Nếu job tắt lâu thì hai bước chạy nối tiếp. Mỗi dấu thời gian chỉ ghi một lần cho mỗi kỳ (DMS-02).
    /// Trả true nếu có thay đổi cần lưu.
    /// </summary>
    private static bool MoveToNextStatus(CheckInPeriod period, DateTimeOffset now)
    {
        HandoverPlan plan = period.Plan;
        bool changed = false;
        if (plan.Status == PlanStatus.Active && now >= period.DueAt)
        {
            period.OverdueAt = now;
            plan.Status = PlanStatus.CheckinOverdue;
            plan.UpdatedAt = now;
            changed = true;
        }
        if (plan.Status == PlanStatus.CheckinOverdue && now >= period.GraceEndsAt)
        {
            period.AwaitingCheckAt = now;
            plan.Status = PlanStatus.AwaitingCheck;
            plan.UpdatedAt = now;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Chỉ gửi mốc nhắc mới nhất đã tới; các mốc cũ hơn chưa gửi được đánh dấu Skipped để không dội email.
    /// Ghi dòng Pending trước khi gửi: gửi lỗi thì dòng vẫn còn và vòng sau thử lại, không bao giờ có hai dòng
    /// cùng (kỳ, mốc) (TIME-02).
    /// </summary>
    private async Task SendLatestReminderAsync(CheckInPeriod period, DateTimeOffset now, CancellationToken ct)
    {
        CheckInReminderKind? latest = CheckInTimeline.LatestReachedMilestone(period.DueAt, period.GraceEndsAt, now);
        if (latest == null)
        {
            return;
        }

        bool changed = false;
        foreach (CheckInReminderKind kind in Enum.GetValues<CheckInReminderKind>())
        {
            if ((int)kind >= (int)latest.Value)
            {
                continue;
            }
            CheckInReminder? older = FindReminder(period, kind);
            if (older == null)
            {
                AddReminder(period, kind, CheckInReminderStatus.Skipped, now);
                changed = true;
            }
            else if (older.Status == CheckInReminderStatus.Pending)
            {
                older.Status = CheckInReminderStatus.Skipped;
                changed = true;
            }
        }

        CheckInReminder? reminder = FindReminder(period, latest.Value);
        if (reminder == null)
        {
            reminder = AddReminder(period, latest.Value, CheckInReminderStatus.Pending, now);
            changed = true;
        }
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        if (reminder.Status != CheckInReminderStatus.Pending)
        {
            return;
        }

        User owner = period.Plan.Vault.Owner;
        await reminderSender.SendReminderAsync(
            owner.Email, owner.FullName, reminder.Kind, period.DueAt, period.GraceEndsAt, ct);

        reminder.Status = CheckInReminderStatus.Sent;
        reminder.SentAt = timeProvider.GetUtcNow();
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static CheckInReminder? FindReminder(CheckInPeriod period, CheckInReminderKind kind)
    {
        foreach (CheckInReminder reminder in period.Reminders)
        {
            if (reminder.Kind == kind)
            {
                return reminder;
            }
        }
        return null;
    }

    private CheckInReminder AddReminder(
        CheckInPeriod period, CheckInReminderKind kind, CheckInReminderStatus status, DateTimeOffset now)
    {
        CheckInReminder reminder = new CheckInReminder();
        reminder.PeriodId = period.Id;
        reminder.Period = period;
        reminder.Kind = kind;
        reminder.Status = status;
        reminder.CreatedAt = now;
        repository.AddReminder(reminder);
        return reminder;
    }
}
