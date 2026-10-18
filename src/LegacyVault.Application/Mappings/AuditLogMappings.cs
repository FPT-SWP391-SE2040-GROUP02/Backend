using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class AuditLogMappings
{
    public static AuditLogResponse ToResponse(this AuditLog log)
    {
        AuditLogResponse response = new AuditLogResponse();
        response.Id = log.Id;
        response.At = log.At;
        response.ActorRole = log.ActorRole.ToApiString();
        response.ActorUserId = log.ActorUserId;
        response.Action = log.Action.ToApiString();
        response.TargetType = log.TargetType.ToApiString();
        response.TargetId = log.TargetId;
        response.TargetVersion = log.TargetVersion;
        response.Outcome = log.Outcome.ToApiString();
        response.Reason = log.Reason;
        return response;
    }
}
