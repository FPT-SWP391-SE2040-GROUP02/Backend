namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Phiên đăng nhập hết hạn hoặc không hợp lệ (401 SESSION_EXPIRED, NFR-06).
/// </summary>
public sealed class SessionExpiredException : ApiException
{
    public SessionExpiredException()
        : base(401, "SESSION_EXPIRED", "Phiên đăng nhập đã hết hạn hoặc không hợp lệ.")
    {
    }
}
