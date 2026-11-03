using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class SystemConfigRepository : ISystemConfigRepository
{
    private readonly AppDbContext context;

    public SystemConfigRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<SystemConfig?> FindAsync(string serviceKey, SystemConfigSlot slot, CancellationToken ct)
    {
        return context.SystemConfigs.FirstOrDefaultAsync(c => c.ServiceKey == serviceKey && c.Slot == slot, ct);
    }

    public Task<List<SystemConfig>> ListAsync(CancellationToken ct)
    {
        return context.SystemConfigs.ToListAsync(ct);
    }

    public void Add(SystemConfig row)
    {
        context.SystemConfigs.Add(row);
    }

    public void Remove(SystemConfig row)
    {
        context.SystemConfigs.Remove(row);
    }
}
