namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Tạm khóa đăng nhập do sai quá 5 lần trong 15 phút (423 ACCOUNT_LOCKED, SEC-10).
/// </summary>
public sealed class AccountLockedException : ApiException
{
    public AccountLockedException(int remainingSeconds)
        : base(423, "ACCOUNT_LOCKED", "Tài khoản tạm thời bị khóa do đăng nhập sai quá số lần quy định.")
    {
        ExtraData["attemptsRemaining"] = 0;
        ExtraData["remainingSeconds"] = remainingSeconds;
    }
}
