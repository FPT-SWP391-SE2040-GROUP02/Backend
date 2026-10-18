using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class OperationsMapper
{
    public static IncidentSummaryResponse ToSummary(Incident incident, DateTimeOffset now)
    {
        IncidentSummaryResponse response = new IncidentSummaryResponse();
        Fill(response, incident, now);
        return response;
    }

    public static IncidentDetailResponse ToDetail(Incident incident, DateTimeOffset now)
    {
        IncidentDetailResponse response = new IncidentDetailResponse();
        Fill(response, incident, now);
        response.RemindedCount = incident.RemindedCount;
        response.EscalatedAt = incident.EscalatedAt;
        response.ResolvedAt = incident.ResolvedAt;
        response.ResolutionNote = incident.ResolutionNote;
        List<IncidentEvent> ordered = new List<IncidentEvent>(incident.Events);
        ordered.Sort((left, right) => left.At.CompareTo(right.At));
        foreach (IncidentEvent incidentEvent in ordered)
        {
            IncidentEventResponse item = new IncidentEventResponse();
            item.Kind = incidentEvent.Kind.ToApiString();
            item.ActorUserId = incidentEvent.ActorUserId;
            item.Note = incidentEvent.Note;
            item.At = incidentEvent.At;
            response.Events.Add(item);
        }
        return response;
    }

    private static void Fill(IncidentSummaryResponse response, Incident incident, DateTimeOffset now)
    {
        response.Id = incident.Id;
        response.Type = incident.Type.ToApiString();
        response.Status = incident.Status.ToApiString();
        response.VaultId = incident.VaultId;
        response.TargetType = incident.TargetType;
        response.TargetId = incident.TargetId;
        response.Summary = incident.Summary;
        response.AssigneeUserId = incident.AssigneeUserId;
        response.DueAt = incident.DueAt;
        response.Overdue = incident.Status != IncidentStatus.Resolved && now >= incident.DueAt;
        response.NextReviewAt = incident.NextReviewAt;
        response.CreatedAt = incident.CreatedAt;
        response.Etag = Etag.From(incident.UpdatedAt);
    }

    public static HoldResponse ToResponse(Hold hold, DateTimeOffset now)
    {
        HoldResponse response = new HoldResponse();
        response.Id = hold.Id;
        response.VaultId = hold.VaultId;
        response.Scope = hold.Scope.ToApiString();
        response.TargetId = hold.TargetId;
        response.Type = hold.Type.ToApiString();
        response.Status = hold.Status.ToApiString();
        response.Reason = hold.Reason;
        response.ResponsibleUserId = hold.ResponsibleUserId;
        response.ReviewAt = hold.ReviewAt;
        response.ReviewOverdue = hold.Status == HoldStatus.Active && now >= hold.ReviewAt;
        response.CreatedAt = hold.CreatedAt;
        response.ReleasedAt = hold.ReleasedAt;
        response.ReleaseReason = hold.ReleaseReason;
        response.Etag = Etag.From(hold.UpdatedAt);
        return response;
    }

    public static AdminUserResponse ToAdminUser(User user)
    {
        AdminUserResponse response = new AdminUserResponse();
        response.Id = user.Id;
        response.Email = user.Email;
        response.FullName = user.FullName;
        response.Role = user.Role.ToApiString();
        response.Status = user.Status.ToApiString();
        response.EmailVerified = user.EmailVerifiedAt != null;
        response.CreatedAt = user.CreatedAt;
        response.LastLoginAt = user.LastLoginAt;
        response.Etag = Etag.From(user.UpdatedAt);
        return response;
    }
}
