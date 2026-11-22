using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class OperationsMapper
{
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
