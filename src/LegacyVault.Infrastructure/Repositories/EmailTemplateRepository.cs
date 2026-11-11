using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class EmailTemplateRepository : IEmailTemplateRepository
{
    private readonly AppDbContext context;

    public EmailTemplateRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<List<EmailTemplate>> ListAsync(CancellationToken ct)
    {
        return await context.EmailTemplates.AsNoTracking().ToListAsync(ct);
    }

    public async Task<EmailTemplate?> FindAsync(string key, CancellationToken ct)
    {
        return await context.EmailTemplates.FirstOrDefaultAsync(t => t.Key == key, ct);
    }

    public void Add(EmailTemplate template)
    {
        context.EmailTemplates.Add(template);
    }

    public void Remove(EmailTemplate template)
    {
        context.EmailTemplates.Remove(template);
    }
}
