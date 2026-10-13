namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Sai email hoặc mật khẩu (401 INVALID_CREDENTIALS).
/// </summary>
public sealed class InvalidCredentialsException : ApiException
{
    public InvalidCredentialsException(int? attemptsRemaining = null)
        : base(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu chưa đúng.")
    {
        if (attemptsRemaining.HasValue)
        {
            ExtraData["attemptsRemaining"] = attemptsRemaining.Value;
        }
    }
}
