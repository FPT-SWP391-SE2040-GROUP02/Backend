namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Xác thực lại khi đổi phương thức đăng nhập thất bại (401 REAUTHENTICATION_FAILED, AUTH-03).
/// Phiên đăng nhập vẫn còn, FE không được coi đây là hết phiên.
/// </summary>
public sealed class ReauthenticationFailedException : ApiException
{
    public ReauthenticationFailedException()
        : base(401, "REAUTHENTICATION_FAILED", "Xác thực lại không thành công. Vui lòng kiểm tra và thử lại.")
    {
    }
}
