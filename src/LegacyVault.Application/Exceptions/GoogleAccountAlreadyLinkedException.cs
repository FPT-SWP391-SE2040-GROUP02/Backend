namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Tài khoản Google này đã gắn với tài khoản khác, hoặc tài khoản app đã gắn một Google khác
/// (409 GOOGLE_ACCOUNT_ALREADY_LINKED).
/// </summary>
public sealed class GoogleAccountAlreadyLinkedException : ApiException
{
    public GoogleAccountAlreadyLinkedException()
        : base(409, "GOOGLE_ACCOUNT_ALREADY_LINKED", "Tài khoản Google này đã được liên kết trước đó.")
    {
    }
}
