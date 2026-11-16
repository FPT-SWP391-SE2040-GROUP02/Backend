using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Application.Services;

public sealed class CheckInJob : ICheckInJob
{
    private readonly ICheckInRepository repository;
    private readonly ICheckInReminderSender reminderSender;
    private readonly ICheckInOverdueNoticeSender overdueNoticeSender;
    private readonly IExecutorReader executorReader;
    private readonly IUserRepository userRepository;
    private readonly ILogger<CheckInJob> logger;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public CheckInJob(
        ICheckInRepository repository,
        ICheckInReminderSender reminderSender,
        ICheckInOverdueNoticeSender overdueNoticeSender,
        IExecutorReader executorReader,
        IUserRepository userRepository,
        ILogger<CheckInJob> logger,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.repository = repository;
        this.reminderSender = reminderSender;
        this.overdueNoticeSender = overdueNoticeSender;
        this.executorReader = executorReader;
        this.userRepository = userRepository;
        this.logger = logger;
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
            // DMS-05: đầu hàm đã loại kế hoạch không ở Active/CheckinOverdue, nên tới đây kế hoạch vừa sang
            // AWAITING_CHECK ngay trong lần chạy này và đã lưu: đây là lần duy nhất báo Executor cho kỳ này.
            await NotifyExecutorsAsync(period, ct);
            // Đã giao việc cho Executor; không nhắc Owner nữa.
            return;
        }
        await SendLatestReminderAsync(period, now, ct);
    }

    /// <summary>
    /// DMS-05: báo từng Executor nhận được việc của kho rằng chủ kho quá hạn điểm danh. Gọi sau khi đã lưu trạng thái.
    /// Không có Executor thì chỉ ghi log cảnh báo. Gửi lỗi chỉ ghi log, không ném và không hoàn tác việc đổi trạng thái.
    /// </summary>
    private async Task NotifyExecutorsAsync(CheckInPeriod period, CancellationToken ct)
    {
        Guid vaultId = period.Plan.VaultId;
        try
        {
            List<Executor> executors = await executorReader.GetAvailableAsync(vaultId, ct);
            if (executors.Count == 0)
            {
                logger.LogWarning("Kho {VaultId} quá hạn điểm danh nhưng chưa có Executor hiệu lực để báo.", vaultId);
                return;
            }
            foreach (Executor executor in executors)
            {
                if (executor.UserId == null)
                {
                    continue;
                }
                User? user = await userRepository.FindByIdAsync(executor.UserId.Value, ct);
                if (user == null || string.IsNullOrWhiteSpace(user.Email) || user.Status != UserStatus.Active)
                {
                    continue;
                }
                CheckInOverdueNotice notice = new CheckInOverdueNotice();
                notice.ToEmail = user.Email;
                notice.FullName = user.FullName;
                notice.VaultId = vaultId;
                notice.DueAt = period.DueAt;
                await overdueNoticeSender.SendAsync(notice, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Không ghi chi tiết lỗi (có thể chứa email).
            logger.LogWarning("Gửi thư báo quá hạn điểm danh cho Executor kho {VaultId} lỗi: {ExceptionType}",
                vaultId, ex.GetType().Name);
        }
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
        IReadOnlyList<int> dueDays = CheckInRules.DueReminderDays;
        CheckInReminderKind? latest = CheckInTimeline.LatestReachedMilestone(
            period.DueAt, period.MaxLateReminders, period.LateReminderGapDays, now, dueDays);
        if (latest == null)
        {
            return;
        }

        bool changed = false;
        IReadOnlyList<CheckInReminderKind> kinds = CheckInTimeline.ActiveKinds(dueDays.Count, period.MaxLateReminders);
        int latestIndex = -1;
        for (int i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] == latest.Value)
            {
                latestIndex = i;
            }
        }
        for (int i = 0; i < latestIndex; i++)
        {
            CheckInReminderKind kind = kinds[i];
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
