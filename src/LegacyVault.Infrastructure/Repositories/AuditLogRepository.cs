using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class AuditLogRepository(AppDbContext context) : IAuditLogRepository
{
    public void Add(AuditLog log)
    {
        context.AuditLogs.Add(log);
    }
}
