namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Tài khoản không ở trạng thái Active (403 ACCOUNT_NOT_ACTIVE).
/// </summary>
public sealed class AccountNotActiveException : ApiException
{
    public AccountNotActiveException()
        : base(403, "ACCOUNT_NOT_ACTIVE", "Tài khoản chưa được kích hoạt hoặc đã bị vô hiệu hóa.")
    {
    }
}
