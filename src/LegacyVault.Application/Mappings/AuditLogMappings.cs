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

    /// <summary>Dòng nhật ký cho Admin: kèm kho và tên, email người thực hiện lấy từ actors (không có thì để null).</summary>
    public static AdminAuditLogResponse ToAdminResponse(AuditLog log, IReadOnlyDictionary<Guid, User> actors)
    {
        AdminAuditLogResponse response = new AdminAuditLogResponse();
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
        response.VaultId = log.VaultId;
        if (log.ActorUserId != null && actors.TryGetValue(log.ActorUserId.Value, out User? actor))
        {
            response.ActorFullName = actor.FullName;
            response.ActorEmail = actor.Email;
        }
        return response;
    }
}
