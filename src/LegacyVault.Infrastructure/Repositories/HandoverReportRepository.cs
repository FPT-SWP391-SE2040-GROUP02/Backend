using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <summary>Đọc và ghi hồ sơ báo tin (EF Core).</summary>
public sealed class HandoverReportRepository : IHandoverReportRepository
{
    private readonly AppDbContext context;

    public HandoverReportRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<HandoverReport?> FindByIdAsync(Guid reportId, CancellationToken ct)
    {
        return context.Set<HandoverReport>()
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct);
    }

    public Task<List<HandoverReport>> ListBySubmitterAsync(Guid userId, CancellationToken ct)
    {
        return ReadOnly()
            .Where(r => r.SubmitterUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<List<HandoverReport>> ListByVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        return ReadOnly()
            .Where(r => r.VerifierUserId == verifierUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<List<HandoverReport>> ListByRecordAsync(Guid recordId, CancellationToken ct)
    {
        return ReadOnly()
            .Where(r => r.RecordId == recordId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<int> CountPendingByVaultAsync(Guid vaultId, Guid? exceptReportId, CancellationToken ct)
    {
        IQueryable<HandoverReport> query = context.Set<HandoverReport>()
            .AsNoTracking()
            .Where(r => r.VaultId == vaultId
                && (r.Status == HandoverClaimStatus.Submitted || r.Status == HandoverClaimStatus.SupplementRequested));
        if (exceptReportId != null)
        {
            Guid excluded = exceptReportId.Value;
            query = query.Where(r => r.Id != excluded);
        }
        return query.CountAsync(ct);
    }

    public void Add(HandoverReport report)
    {
        context.Set<HandoverReport>().Add(report);
    }

    public void AddFile(HandoverReportFile file)
    {
        context.Set<HandoverReportFile>().Add(file);
    }

    private IQueryable<HandoverReport> ReadOnly()
    {
        return context.Set<HandoverReport>().AsNoTracking().Include(r => r.Files);
    }
}
