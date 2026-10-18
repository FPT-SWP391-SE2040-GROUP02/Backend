using System.Security.Claims;
using LegacyVault.Application.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LegacyVault.Infrastructure.Security;

/// <summary>Cho biết ai đang gọi API, đọc từ JWT đã được kiểm chữ ký và hạn ở bước xác thực.</summary>
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

            // Claim "sub" chứa Id người dùng do JwtTokenService đặt khi cấp token.
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
