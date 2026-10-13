using System.Security.Claims;
using LegacyVault.Application.Security;

namespace LegacyVault.Api.Security;

/// <summary>
/// Đọc người đang gọi API từ HttpContext.User (đã qua middleware xác thực).
/// Không phụ thuộc cơ chế xác thực: dev header hôm nay, JWT Bearer sau này — miễn là có claim "sub"/NameIdentifier.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            HttpContext? context = httpContextAccessor.HttpContext;
            if (context == null)
            {
                return null;
            }

            string? value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (value == null)
            {
                value = context.User.FindFirstValue("sub");
            }

            Guid id;
            if (Guid.TryParse(value, out id))
            {
                return id;
            }
            return null;
        }
    }

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
