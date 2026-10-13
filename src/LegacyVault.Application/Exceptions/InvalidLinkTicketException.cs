namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Vé liên kết Google sai, hết hạn hoặc đã dùng (400 INVALID_LINK_TICKET, AUTH-02).
/// </summary>
public sealed class InvalidLinkTicketException : ApiException
{
    public InvalidLinkTicketException()
        : base(400, "INVALID_LINK_TICKET", "Yêu cầu liên kết Google không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập Google lại.")
    {
    }
}
