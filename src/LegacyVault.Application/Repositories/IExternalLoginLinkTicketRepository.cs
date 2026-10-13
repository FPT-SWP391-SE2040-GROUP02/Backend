using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng ExternalLoginLinkTickets (vé chờ liên kết Google, AUTH-02).</summary>
public interface IExternalLoginLinkTicketRepository
{
    /// <summary>Tìm theo hash của vé, kèm User. Không thấy → null.</summary>
    Task<ExternalLoginLinkTicket?> FindByHashAsync(string ticketHash, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT".</summary>
    void Add(ExternalLoginLinkTicket ticket);
}
