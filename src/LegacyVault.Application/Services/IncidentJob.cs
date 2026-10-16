using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Application.Mappings;

namespace LegacyVault.Application.Services;

public interface IIncidentJob
{
    Task<List<Guid>> FindIdsAsync(CancellationToken ct);

    /// <summary>
    /// Xử lý một sự cố: đóng sự cố điểm danh khi kỳ đã đóng, giao người cho sự cố chưa có ai, nhắc người phụ trách sau 48 giờ,
    /// báo Admin sau 72 giờ. Trả mã việc đã làm (RESOLVED, ASSIGNED, REMINDED, ESCALATED) hoặc null. Chạy lặp an toàn.
    /// </summary>
    Task<string?> ProcessAsync(Guid incidentId, CancellationToken ct);
}

/// <remarks>
/// - AC-40: việc chờ luôn có người, hạn và nhắc; quá hạn không tự cho đạt hay tự đóng.
/// - Thư gửi lỗi không làm hỏng việc: không đánh dấu đã nhắc/đã báo nên vòng sau thử lại.
/// - Thư chỉ có loại sự cố và hạn, không có nội dung kho hay định danh (SEC-07).
/// </remarks>
public sealed class IncidentJob : IIncidentJob
{
    private const int BatchSize = 50;

    private readonly IIncidentRepository incidents;
    private readonly IUserRepository users;
    private readonly IAdminUserRepository adminUsers;
    private readonly IOperationsRecorder recorder;
    private readonly IIncidentNoticeSender sender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public IncidentJob(
        IIncidentRepository incidents,
        IUserRepository users,
        IAdminUserRepository adminUsers,
        IOperationsRecorder recorder,
        IIncidentNoticeSender sender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.incidents = incidents;
        this.users = users;
        this.adminUsers = adminUsers;
        this.recorder = recorder;
        this.sender = sender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<List<Guid>> FindIdsAsync(CancellationToken ct)
    {
        return incidents.FindIdsForJobAsync(timeProvider.GetUtcNow(), BatchSize, ct);
    }

    public async Task<string?> ProcessAsync(Guid incidentId, CancellationToken ct)
    {
        Incident? incident = await incidents.FindByIdAsync(incidentId, ct);
        if (incident == null || incident.Status == IncidentStatus.Resolved)
        {
            return null;
        }

        // Sự cố điểm danh theo dõi kỳ: kỳ đã đóng (Owner điểm danh) thì đóng sự cố.
        if (incident.TargetType == OperationsRecorder.TargetCheckInPeriod && incident.TargetId != null)
        {
            await recorder.RecordCheckInPeriodAsync(incident.TargetId.Value, ct);
            incident = await incidents.FindByIdAsync(incidentId, ct);
            if (incident == null || incident.Status == IncidentStatus.Resolved)
            {
                return "RESOLVED";
            }
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (incident.AssigneeUserId == null)
        {
            Guid? admin = await incidents.FindLeastLoadedAdminAsync(ct);
            if (admin == null)
            {
                // Vẫn "bị kẹt": đẩy ngày xem lại để job và dashboard không bỏ sót.
                incident.NextReviewAt = now + IncidentRules.UnassignedReview;
                incident.UpdatedAt = now;
                await unitOfWork.SaveChangesAsync(ct);
                return null;
            }
            incident.AssigneeUserId = admin;
            incident.Status = IncidentStatus.InProgress;
            incident.DueAt = now + IncidentRules.ReminderAfter;
            incident.NextReviewAt = now + IncidentRules.MaxReviewInterval;
            incident.UpdatedAt = now;
            AddEvent(incident, IncidentEventKind.Assigned, "Tự giao cho Admin ít việc nhất.", now);
            await unitOfWork.SaveChangesAsync(ct);
            await TrySendAsync(incident, admin.Value, IncidentNoticeKind.Assigned, ct);
            return "ASSIGNED";
        }

        string? outcome = null;
        if (incident.RemindedCount == 0 && now >= incident.DueAt)
        {
            if (await TrySendAsync(incident, incident.AssigneeUserId.Value, IncidentNoticeKind.Reminder, ct))
            {
                incident.RemindedCount = 1;
                incident.UpdatedAt = now;
                AddEvent(incident, IncidentEventKind.Reminded, null, now);
                await unitOfWork.SaveChangesAsync(ct);
                outcome = "REMINDED";
            }
        }

        if (incident.EscalatedAt == null && now >= incident.CreatedAt + IncidentRules.EscalateAfter)
        {
            (List<User> admins, int total) = await adminUsers.ListAsync(null, UserRole.Admin, UserStatus.Active, 1, 20, ct);
            bool sentAny = false;
            foreach (User admin in admins)
            {
                if (await TrySendAsync(incident, admin.Id, IncidentNoticeKind.Escalation, ct))
                {
                    sentAny = true;
                }
            }
            if (sentAny)
            {
                incident.EscalatedAt = now;
                incident.UpdatedAt = now;
                AddEvent(incident, IncidentEventKind.Escalated, null, now);
                await unitOfWork.SaveChangesAsync(ct);
                outcome = "ESCALATED";
            }
        }
        return outcome;
    }

    private void AddEvent(Incident incident, IncidentEventKind kind, string? note, DateTimeOffset now)
    {
        IncidentEvent incidentEvent = new IncidentEvent();
        incidentEvent.IncidentId = incident.Id;
        incidentEvent.Kind = kind;
        incidentEvent.Note = note;
        incidentEvent.At = now;
        incident.Events.Add(incidentEvent);
        incidents.AddEvent(incidentEvent);
    }

    private async Task<bool> TrySendAsync(Incident incident, Guid userId, IncidentNoticeKind kind, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || string.IsNullOrWhiteSpace(user.Email) || user.Status != UserStatus.Active)
        {
            return false;
        }
        IncidentNotice notice = new IncidentNotice();
        notice.Kind = kind;
        notice.ToEmail = user.Email;
        notice.FullName = user.FullName;
        notice.IncidentId = incident.Id;
        notice.IncidentType = incident.Type.ToApiString();
        notice.DueAt = incident.DueAt;
        try
        {
            await sender.SendAsync(notice, ct);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
