using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class ExternalLoginLinkTicketRepository : IExternalLoginLinkTicketRepository
{
    private readonly AppDbContext _db;

    public ExternalLoginLinkTicketRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<ExternalLoginLinkTicket?> FindByHashAsync(string ticketHash, CancellationToken ct)
    {
        return _db.ExternalLoginLinkTickets
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TicketHash == ticketHash, ct);
    }

    public void Add(ExternalLoginLinkTicket ticket)
    {
        _db.ExternalLoginLinkTickets.Add(ticket);
    }
}
