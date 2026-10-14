using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.CheckIn;

// Các class giả để test điểm danh mà không cần SQL Server. Dữ liệu chỉ nằm trong List.

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated
    {
        get { return UserId != null; }
    }
}

public sealed class FakeVaultRepository : IVaultRepository
{
    public List<Vault> Vaults { get; } = new List<Vault>();

    public Task<Vault?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return Task.FromResult(Vaults.FirstOrDefault(v => v.Id == id));
    }

    public Task<Vault?> FindByOwnerIdAsync(Guid ownerId, CancellationToken ct)
    {
        return Task.FromResult(Vaults.FirstOrDefault(v => v.OwnerId == ownerId));
    }

    public Task<bool> TryAddAsync(Vault vault, CancellationToken ct)
    {
        Vaults.Add(vault);
        return Task.FromResult(true);
    }

    public Task<long> SumUsedBytesAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(0L);
    }
}

public sealed class FakeCheckInRepository : ICheckInRepository
{
    private static readonly TimeSpan EarliestReminderLead = TimeSpan.FromDays(7);

    public List<CheckInSchedule> Schedules { get; } = new List<CheckInSchedule>();
    public List<CheckInPeriod> Periods { get; } = new List<CheckInPeriod>();
    public List<CheckInReminder> Reminders { get; } = new List<CheckInReminder>();

    public Task<CheckInSchedule?> FindScheduleByPlanIdAsync(Guid planId, CancellationToken ct)
    {
        return Task.FromResult(Schedules.FirstOrDefault(s => s.PlanId == planId));
    }

    public Task<CheckInPeriod?> FindOpenPeriodByPlanIdAsync(Guid planId, CancellationToken ct)
    {
        return Task.FromResult(Periods.FirstOrDefault(p => p.PlanId == planId && p.ClosedAt == null));
    }

    public Task<int> GetMaxSequenceAsync(Guid planId, CancellationToken ct)
    {
        int max = 0;
        foreach (CheckInPeriod period in Periods.Where(p => p.PlanId == planId))
        {
            if (period.Sequence > max)
            {
                max = period.Sequence;
            }
        }
        return Task.FromResult(max);
    }

    public Task<(List<CheckInPeriod> Items, int TotalCount)> FindPeriodPageByPlanIdAsync(
        Guid planId, int pageIndex, int pageSize, CancellationToken ct)
    {
        List<CheckInPeriod> all = Periods.Where(p => p.PlanId == planId).OrderByDescending(p => p.Sequence).ToList();
        List<CheckInPeriod> page = all.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((page, all.Count));
    }

    public Task<IReadOnlyList<Guid>> FindOpenPeriodIdsForJobAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> ids = new List<Guid>();
        foreach (CheckInPeriod period in Periods)
        {
            bool statusOk = period.Plan.Status == PlanStatus.Active || period.Plan.Status == PlanStatus.CheckinOverdue;
            if (period.ClosedAt == null && statusOk && period.DueAt <= now + EarliestReminderLead)
            {
                ids.Add(period.Id);
            }
        }
        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    public Task<CheckInPeriod?> FindPeriodForJobAsync(Guid periodId, CancellationToken ct)
    {
        return Task.FromResult(Periods.FirstOrDefault(p => p.Id == periodId));
    }

    public void AddSchedule(CheckInSchedule schedule)
    {
        Schedules.Add(schedule);
    }

    public void AddPeriod(CheckInPeriod period)
    {
        Periods.Add(period);
    }

    public void AddReminder(CheckInReminder reminder)
    {
        Reminders.Add(reminder);
        reminder.Period.Reminders.Add(reminder);
    }
}

public sealed class FakeCheckInReminderSender : ICheckInReminderSender
{
    public List<CheckInReminderKind> Sent { get; } = new List<CheckInReminderKind>();

    /// <summary>Đặt true thì lần gọi kế tiếp ném lỗi (rồi tự tắt) để thử luồng gửi lỗi.</summary>
    public bool FailNext { get; set; }

    public Task SendReminderAsync(
        string toEmail, string fullName, CheckInReminderKind kind,
        DateTimeOffset dueAt, DateTimeOffset graceEndsAt, CancellationToken ct)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("Gửi nhắc thất bại (giả lập).");
        }
        Sent.Add(kind);
        return Task.CompletedTask;
    }
}

public static class CheckInTestData
{
    /// <summary>Tạo kho của một Owner kèm kế hoạch ở trạng thái cho trước (Plan, Entitlement, Owner đều đã gắn).</summary>
    public static Vault CreateVault(Guid ownerId, PlanStatus planStatus)
    {
        User owner = new User();
        owner.Id = ownerId;
        owner.Email = "owner@test.local";
        owner.FullName = "Chủ kho thử";

        Vault vault = new Vault();
        vault.OwnerId = ownerId;
        vault.Owner = owner;
        vault.Status = VaultStatus.Open;
        vault.CreatedAt = DateTimeOffset.UtcNow;

        HandoverPlan plan = new HandoverPlan();
        plan.VaultId = vault.Id;
        plan.Vault = vault;
        plan.Status = planStatus;
        plan.ConfigVersion = 1;
        plan.CreatedAt = DateTimeOffset.UtcNow;
        vault.Plan = plan;

        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.VaultId = vault.Id;
        entitlement.Vault = vault;
        entitlement.Status = EntitlementStatus.PaidActive;
        vault.Entitlement = entitlement;
        return vault;
    }

    /// <summary>Tạo một kỳ đang mở với hạn tính theo chu kỳ/thời gian chờ cho trước.</summary>
    public static CheckInPeriod CreateOpenPeriod(
        HandoverPlan plan, int sequence, DateTimeOffset startedAt, int cycleDays, int graceDays)
    {
        CheckInPeriod period = new CheckInPeriod();
        period.PlanId = plan.Id;
        period.Plan = plan;
        period.Sequence = sequence;
        period.StartedAt = startedAt;
        period.DueAt = startedAt.AddDays(cycleDays);
        period.GraceEndsAt = period.DueAt.AddDays(graceDays);
        return period;
    }
}
