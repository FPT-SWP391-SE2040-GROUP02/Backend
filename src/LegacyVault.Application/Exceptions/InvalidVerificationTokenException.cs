namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Link xác minh email không hợp lệ hoặc đã dùng/hết hạn (400 INVALID_VERIFICATION_TOKEN, AUTH-04).
/// </summary>
public sealed class InvalidVerificationTokenException : ApiException
{
    public InvalidVerificationTokenException()
        : base(400, "INVALID_VERIFICATION_TOKEN", "Link xác minh email không hợp lệ, đã hết hạn hoặc đã được sử dụng.")
    {
    }
}
