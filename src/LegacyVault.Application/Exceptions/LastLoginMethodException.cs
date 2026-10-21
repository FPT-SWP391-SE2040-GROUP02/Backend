namespace LegacyVault.Application.Exceptions;

/// <summary>Không được gỡ phương thức đăng nhập cuối cùng của tài khoản (409 LAST_LOGIN_METHOD, AUTH-03).</summary>
public sealed class LastLoginMethodException : ApiException
{
    public LastLoginMethodException()
        : base(409, "LAST_LOGIN_METHOD", "Đây là phương thức đăng nhập cuối cùng, không thể gỡ.")
    {
    }
}
