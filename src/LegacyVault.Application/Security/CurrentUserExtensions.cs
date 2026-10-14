using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Security;

public static class CurrentUserExtensions
{
    /// <summary>Id người đang đăng nhập. Chưa đăng nhập/token không hợp lệ → 401 SESSION_EXPIRED.</summary>
    public static Guid RequireUserId(this ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired,
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }
        return userId;
    }
}
