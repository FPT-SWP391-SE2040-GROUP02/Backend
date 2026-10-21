namespace LegacyVault.Application.Exceptions;

/// <summary>Tài khoản đã có phương thức đăng nhập này (409 LOGIN_METHOD_ALREADY_EXISTS, AUTH-03).</summary>
public sealed class LoginMethodAlreadyExistsException : ApiException
{
    public LoginMethodAlreadyExistsException()
        : base(409, "LOGIN_METHOD_ALREADY_EXISTS", "Tài khoản đã có phương thức đăng nhập này.")
    {
    }
}
