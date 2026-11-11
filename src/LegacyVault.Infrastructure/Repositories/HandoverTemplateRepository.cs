using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class HandoverTemplateRepository : IHandoverTemplateRepository
{
    private readonly AppDbContext context;

    public HandoverTemplateRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<HandoverTemplateSet?> FindLatestAsync(CancellationToken ct)
    {
        return await context.HandoverTemplateSets.AsNoTracking().OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<HandoverTemplateSet?> FindByIdAsync(int version, CancellationToken ct)
    {
        return await context.HandoverTemplateSets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == version, ct);
    }

    public async Task<List<HandoverTemplateSet>> ListLatestAsync(int count, CancellationToken ct)
    {
        return await context.HandoverTemplateSets.AsNoTracking().OrderByDescending(t => t.Id).Take(count).ToListAsync(ct);
    }

    public void Add(HandoverTemplateSet set)
    {
        context.HandoverTemplateSets.Add(set);
    }
}
