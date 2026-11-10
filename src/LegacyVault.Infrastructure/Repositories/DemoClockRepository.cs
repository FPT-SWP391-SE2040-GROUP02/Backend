using System.Globalization;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class DemoClockRepository : IDemoClockRepository
{
    /// <summary>Khóa dòng cấu hình lưu độ lệch đồng hồ DEMO (giây).</summary>
    public const string OffsetKey = "Dev.DemoClock.OffsetSeconds";

    private readonly AppDbContext context;

    public DemoClockRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<long?> FindOffsetSecondsAsync(CancellationToken ct)
    {
        SystemSetting? row = await context.Set<SystemSetting>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == OffsetKey, ct);
        if (row == null)
        {
            return null;
        }
        long seconds;
        if (!long.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
        {
            return null;
        }
        return seconds;
    }

    public async Task SaveOffsetSecondsAsync(long offsetSeconds, DateTimeOffset now, CancellationToken ct)
    {
        SystemSetting? row = await context.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == OffsetKey, ct);
        if (row == null)
        {
            row = new SystemSetting();
            row.Key = OffsetKey;
            row.DataType = "NUMBER";
            row.Category = "General";
            row.Description = "Độ lệch đồng hồ DEMO so với giờ thật, tính bằng giây (chỉ Development/Staging).";
            row.IsEnabled = true;
            context.Set<SystemSetting>().Add(row);
        }
        row.Value = offsetSeconds.ToString(CultureInfo.InvariantCulture);
        row.UpdatedAt = now;
        row.UpdatedBy = "demo-clock";
        await context.SaveChangesAsync(ct);
    }

    public async Task<List<DemoOpenPeriod>> ListOpenCheckInPeriodsAsync(CancellationToken ct)
    {
        List<DemoOpenPeriod> result = await context.Set<CheckInPeriod>()
            .AsNoTracking()
            .Where(p => p.ClosedAt == null
                && (p.Plan.Status == PlanStatus.Active || p.Plan.Status == PlanStatus.CheckinOverdue))
            .Select(p => new DemoOpenPeriod
            {
                DueAt = p.DueAt,
                GraceEndsAt = p.GraceEndsAt,
                MaxLateReminders = p.MaxLateReminders,
                LateReminderGapDays = p.LateReminderGapDays,
            })
            .ToListAsync(ct);
        return result;
    }

    public async Task<List<DemoDeadline>> ListUpcomingDeadlinesAsync(
        DateTimeOffset after, DateTimeOffset until, TimeSpan remindAfter, TimeSpan incidentEscalateAfter,
        CancellationToken ct)
    {
        List<DemoDeadline> result = new List<DemoDeadline>();

        // Lời mời Người thực thi: hạn 7 ngày và thư nhắc ở giờ thứ 48 (chưa gửi nhắc).
        List<DateTimeOffset> invitationExpiries = await context.Set<Executor>()
            .AsNoTracking()
            .Where(e => e.Status == ExecutorStatus.Invited && e.ExpiresAt > after && e.ExpiresAt <= until)
            .Select(e => e.ExpiresAt)
            .ToListAsync(ct);
        AddAll(result, invitationExpiries, "EXECUTOR_INVITATION");

        List<DateTimeOffset> invitedTimes = await context.Set<Executor>()
            .AsNoTracking()
            .Where(e => e.Status == ExecutorStatus.Invited && e.ReminderSentAt == null)
            .Select(e => e.InvitedAt)
            .ToListAsync(ct);
        foreach (DateTimeOffset invitedAt in invitedTimes)
        {
            DateTimeOffset remindAt = invitedAt + remindAfter;
            if (remindAt > after && remindAt <= until)
            {
                AddOne(result, remindAt, "EXECUTOR_INVITATION");
            }
        }

        // Hết hạn dịch vụ của kho trả phí.
        List<DateTimeOffset> serviceExpiries = await context.Set<VaultEntitlement>()
            .AsNoTracking()
            .Where(v => v.Status == EntitlementStatus.PaidActive
                && v.ServiceExpiresAt != null && v.ServiceExpiresAt > after && v.ServiceExpiresAt <= until)
            .Select(v => v.ServiceExpiresAt!.Value)
            .ToListAsync(ct);
        AddAll(result, serviceExpiries, "SERVICE_EXPIRY");

        // Bàn giao: hạn phản hồi, hạn quyết định, hạn tải.
        List<DateTimeOffset> responseDeadlines = await context.Set<HandoverTurn>()
            .AsNoTracking()
            .Where(t => t.Status == HandoverTurnStatus.Invited && t.InvitationSentAt != null
                && t.ResponseDeadline > after && t.ResponseDeadline <= until)
            .Select(t => t.ResponseDeadline)
            .ToListAsync(ct);
        AddAll(result, responseDeadlines, "HANDOVER_DEADLINE");

        List<DateTimeOffset> decisionDeadlines = await context.Set<HandoverTurn>()
            .AsNoTracking()
            .Where(t => t.Status == HandoverTurnStatus.ReadyToAccept && t.DecisionDeadline != null
                && t.DecisionDeadline > after && t.DecisionDeadline <= until)
            .Select(t => t.DecisionDeadline!.Value)
            .ToListAsync(ct);
        AddAll(result, decisionDeadlines, "HANDOVER_DEADLINE");

        List<DateTimeOffset> grantExpiries = await context.Set<HandoverGrant>()
            .AsNoTracking()
            .Where(g => g.Status == HandoverGrantStatus.Active && g.ExpiresAt > after && g.ExpiresAt <= until)
            .Select(g => g.ExpiresAt)
            .ToListAsync(ct);
        AddAll(result, grantExpiries, "HANDOVER_DEADLINE");

        // Sự cố chưa đóng: giờ nhắc người phụ trách và giờ báo lên Admin.
        List<DemoIncidentTimes> incidents = await context.Set<Incident>()
            .AsNoTracking()
            .Where(i => i.Status != IncidentStatus.Resolved)
            .Select(i => new DemoIncidentTimes
            {
                DueAt = i.DueAt,
                CreatedAt = i.CreatedAt,
                NeedsReminder = i.RemindedCount == 0,
                NeedsEscalation = i.EscalatedAt == null,
            })
            .ToListAsync(ct);
        foreach (DemoIncidentTimes incident in incidents)
        {
            if (incident.NeedsReminder && incident.DueAt > after && incident.DueAt <= until)
            {
                AddOne(result, incident.DueAt, "INCIDENT");
            }
            DateTimeOffset escalateAt = incident.CreatedAt + incidentEscalateAfter;
            if (incident.NeedsEscalation && escalateAt > after && escalateAt <= until)
            {
                AddOne(result, escalateAt, "INCIDENT");
            }
        }
        return result;
    }

    public async Task<int> CountSentCheckInRemindersAsync(CancellationToken ct)
    {
        int count = await context.Set<CheckInReminder>()
            .AsNoTracking()
            .CountAsync(r => r.Status == CheckInReminderStatus.Sent, ct);
        return count;
    }

    private static void AddAll(List<DemoDeadline> list, List<DateTimeOffset> times, string kind)
    {
        foreach (DateTimeOffset time in times)
        {
            AddOne(list, time, kind);
        }
    }

    private static void AddOne(List<DemoDeadline> list, DateTimeOffset time, string kind)
    {
        DemoDeadline deadline = new DemoDeadline();
        deadline.At = time;
        deadline.Kind = kind;
        list.Add(deadline);
    }

    /// <summary>Dữ liệu tối thiểu của sự cố để tính giờ nhắc và giờ báo lên.</summary>
    private sealed class DemoIncidentTimes
    {
        public DateTimeOffset DueAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool NeedsReminder { get; set; }
        public bool NeedsEscalation { get; set; }
    }
}
