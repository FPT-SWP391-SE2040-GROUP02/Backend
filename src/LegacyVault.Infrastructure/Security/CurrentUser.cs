using System.Security.Claims;
using LegacyVault.Application.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LegacyVault.Infrastructure.Security;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated
    {
        get
        {
            var principal = GetPrincipal();
            return principal != null
                && principal.Identity != null
                && principal.Identity.IsAuthenticated;
        }
    }

    public Guid? UserId
    {
        get
        {
            var principal = GetPrincipal();
            if (principal == null)
            {
                return null;
            }

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (Guid.TryParse(sub, out var id))
            {
                return id;
            }

            return null;
        }
    }

    private ClaimsPrincipal? GetPrincipal()
    {
        return _httpContextAccessor.HttpContext?.User;
    }
}
