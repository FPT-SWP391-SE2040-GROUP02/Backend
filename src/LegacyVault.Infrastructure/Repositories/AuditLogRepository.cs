using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class AuditLogRepository(AppDbContext context) : IAuditLogRepository
{
    public void Add(AuditLog log)
    {
        context.AuditLogs.Add(log);
    }

    public async Task<(List<AuditLog> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, AuditAction? action, DateTimeOffset? from, DateTimeOffset? to,
        int pageIndex, int pageSize, bool isAscending, CancellationToken ct)
    {
        var query = context.AuditLogs.AsNoTracking().Where(l => l.VaultId == vaultId);
        if (action != null)
        {
            query = query.Where(l => l.Action == action.Value);
        }
        if (from != null)
        {
            query = query.Where(l => l.At >= from.Value);
        }
        if (to != null)
        {
            query = query.Where(l => l.At < to.Value);
        }

        int totalCount = await query.CountAsync(ct);

        // Dùng index (VaultId, At, Id); ThenBy(Id) giữ thứ tự ổn định khi nhiều dòng cùng thời điểm.
        if (isAscending)
        {
            query = query.OrderBy(l => l.At).ThenBy(l => l.Id);
        }
        else
        {
            query = query.OrderByDescending(l => l.At).ThenByDescending(l => l.Id);
        }

        List<AuditLog> items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }
}
