using System.Security.Cryptography;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <summary>Đọc gộp dữ liệu cho màn tiến độ bàn giao của Người thực thi (EF Core).</summary>
/// <remarks>
/// Chỉ truy cập dữ liệu, luật nằm ở HandoverProgressService. Dùng context.Set&lt;T&gt;()
/// để không phải sửa AppDbContext. Mọi truy vấn AsNoTracking vì màn hình này chỉ đọc.
/// </remarks>
public sealed class HandoverProgressReader : IHandoverProgressReader
{
    private readonly AppDbContext context;
    private readonly IBeneficiaryDataProtector protector;

    public HandoverProgressReader(AppDbContext context, IBeneficiaryDataProtector protector)
    {
        this.context = context;
        this.protector = protector;
    }

    public Task<HandoverRecord?> FindRecordAsync(Guid recordId, CancellationToken ct)
    {
        return context.Set<HandoverRecord>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.Signatures)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == recordId, ct);
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

    public async Task<Dictionary<Guid, string>> FindCitizenIdsAsync(
        List<Guid> beneficiaryIds, CancellationToken ct)
    {
        Dictionary<Guid, string> result = new Dictionary<Guid, string>();
        if (beneficiaryIds.Count == 0)
        {
            return result;
        }

        // Giải mã phần danh tính để lấy số CCCD đầy đủ; hồ sơ không giải mã được thì để chuỗi rỗng.
        List<Beneficiary> rows = await context.Set<Beneficiary>()
            .AsNoTracking()
            .Where(b => beneficiaryIds.Contains(b.Id))
            .ToListAsync(ct);

        foreach (Beneficiary row in rows)
        {
            string citizenId = string.Empty;
            try
            {
                citizenId = protector.Unprotect(row).CitizenId;
            }
            catch (CryptographicException)
            {
                citizenId = string.Empty;
            }
            result[row.Id] = citizenId;
        }
        return result;
    }
}
