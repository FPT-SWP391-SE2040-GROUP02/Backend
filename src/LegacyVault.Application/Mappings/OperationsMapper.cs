using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class OperationsMapper
{
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
