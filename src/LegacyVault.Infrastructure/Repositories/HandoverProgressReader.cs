using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Chỉ truy cập dữ liệu, luật nằm ở HandoverProgressService. Dùng context.Set&lt;T&gt;()
/// để không phải sửa AppDbContext. Mọi truy vấn AsNoTracking vì màn hình này chỉ đọc.
/// </remarks>
public sealed class HandoverProgressReader : IHandoverProgressReader
{
    private readonly AppDbContext context;

    public HandoverProgressReader(AppDbContext context)
    {
        this.context = context;
    }

    public Task<DeathCase?> FindCaseAsync(Guid caseId, CancellationToken ct)
    {
        return context.Set<DeathCase>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == caseId, ct);
    }

    public Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .AsNoTracking()
            .Include(t => t.Items)
            .Include(t => t.Grant)
            .Where(t => t.PlanId == planId)
            .OrderBy(t => t.ContainerName)
            .ThenBy(t => t.InvitedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, DateTimeOffset>> FindFirstDownloadTimesAsync(
        List<Guid> grantIds, CancellationToken ct)
    {
        Dictionary<Guid, DateTimeOffset> result = new Dictionary<Guid, DateTimeOffset>();
        if (grantIds.Count == 0)
        {
            return result;
        }

        // Một truy vấn gom nhóm cho mọi quyền tải; chỉ tính lần tải thành công.
        var rows = await context.Set<HandoverAccessLog>()
            .AsNoTracking()
            .Where(l => grantIds.Contains(l.GrantId) && l.Outcome == HandoverAccessOutcome.Succeeded)
            .GroupBy(l => l.SnapshotItemId)
            .Select(g => new { ItemId = g.Key, FirstAt = g.Min(l => l.At) })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            result[row.ItemId] = row.FirstAt;
        }
        return result;
    }

    public async Task<Dictionary<Guid, string>> FindCitizenIdLast4Async(
        List<Guid> beneficiaryIds, CancellationToken ct)
    {
        Dictionary<Guid, string> result = new Dictionary<Guid, string>();
        if (beneficiaryIds.Count == 0)
        {
            return result;
        }

        // Chỉ đọc cột 4 số cuối (không mã hóa); không đọc và không giải mã phần danh tính, liên hệ.
        var rows = await context.Set<Beneficiary>()
            .AsNoTracking()
            .Where(b => beneficiaryIds.Contains(b.Id))
            .Select(b => new { b.Id, b.CitizenIdLast4 })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            result[row.Id] = row.CitizenIdLast4;
        }
        return result;
    }
}
