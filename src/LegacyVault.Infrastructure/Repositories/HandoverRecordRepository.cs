using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <summary>Đọc và ghi biên bản bàn giao (EF Core).</summary>
public sealed class HandoverRecordRepository : IHandoverRecordRepository
{
    private readonly AppDbContext context;

    public HandoverRecordRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<HandoverRecord?> FindByIdAsync(Guid recordId, CancellationToken ct)
    {
        return QueryWithDetails().FirstOrDefaultAsync(r => r.Id == recordId, ct);
    }

    public Task<HandoverRecord?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return QueryWithDetails()
            .FirstOrDefaultAsync(r => r.VaultId == vaultId && r.Status != HandoverRecordStatus.Cancelled, ct);
    }

    public Task<List<HandoverRecord>> ListByVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        return QueryWithDetails()
            .Where(r => r.VerifierUserId == verifierUserId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task<Guid?> FindVerifierCandidateAsync(
        List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct)
    {
        // SQL Server xếp NULL trước khi tăng dần: luật sư chưa từng được giao đứng đầu.
        Guid? candidateId = await context.Set<User>()
            .Where(u => u.Role == UserRole.Verifier
                && u.Status == UserStatus.Active
                && !excludedUserIds.Contains(u.Id)
                && !excludedEmails.Contains(u.Email))
            .Select(u => new
            {
                u.Id,
                LastAssignedAt = context.Set<HandoverRecord>()
                    .Where(r => r.VerifierUserId == u.Id)
                    .Max(r => (DateTimeOffset?)r.RequestedAt),
            })
            .OrderBy(x => x.LastAssignedAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        return candidateId;
    }

    public Task<List<Asset>> ListVaultAssetsAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Asset>()
            .AsNoTracking()
            .Where(a => a.VaultId == vaultId && a.ContentDeletedAt == null)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);
    }

    public Task<List<Beneficiary>> ListVaultBeneficiariesAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Beneficiary>()
            .AsNoTracking()
            .Where(b => b.VaultId == vaultId)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);
    }

    public Task<List<Beneficiary>> ListBeneficiariesOfActivatedPlansAsync(CancellationToken ct)
    {
        return context.Set<Beneficiary>()
            .AsNoTracking()
            .Include(b => b.Vault).ThenInclude(v => v.Owner)
            .Include(b => b.Vault).ThenInclude(v => v.Plan)
            .Where(b => b.Vault.Plan.Status == PlanStatus.Active
                || b.Vault.Plan.Status == PlanStatus.HandoverRequested
                || b.Vault.Plan.Status == PlanStatus.HandoverInProgress
                || b.Vault.Plan.Status == PlanStatus.Closed)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);
    }

    public Task<List<Executor>> ListAcceptedExecutorsAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<Executor>()
            .AsNoTracking()
            .Include(e => e.Vault).ThenInclude(v => v.Owner)
            .Include(e => e.Vault).ThenInclude(v => v.Plan)
            .Where(e => e.UserId == userId && e.Status == ExecutorStatus.Accepted)
            .OrderBy(e => e.AcceptedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
    }

    public Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid recordId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Include(t => t.Items)
            .Include(t => t.Grant)
            .Where(t => t.RecordId == recordId)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public void AddSnapshotItem(HandoverSnapshotItem item)
    {
        context.Set<HandoverSnapshotItem>().Add(item);
    }

    public void RemoveSnapshotItem(HandoverSnapshotItem item)
    {
        context.Set<HandoverSnapshotItem>().Remove(item);
    }

    public void Add(HandoverRecord record)
    {
        context.Set<HandoverRecord>().Add(record);
    }

    public void AddLine(HandoverRecordLine line)
    {
        context.Set<HandoverRecordLine>().Add(line);
    }

    public void RemoveLine(HandoverRecordLine line)
    {
        context.Set<HandoverRecordLine>().Remove(line);
    }

    public void AddSignature(HandoverRecordSignature signature)
    {
        context.Set<HandoverRecordSignature>().Add(signature);
    }

    public Task<Beneficiary?> FindBeneficiaryByRequestTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return context.Set<Beneficiary>()
            .Include(b => b.Vault).ThenInclude(v => v.Plan)
            .FirstOrDefaultAsync(b => b.RequestTokenHash == tokenHash, ct);
    }

    public Task<List<HandoverClaim>> ListClaimsByUserAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<HandoverClaim>()
            .AsNoTracking()
            .Include(c => c.Record)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    public void AddClaim(HandoverClaim claim)
    {
        context.Set<HandoverClaim>().Add(claim);
    }

    public void AddFile(HandoverRecordFile file)
    {
        context.Set<HandoverRecordFile>().Add(file);
    }

    public void RemoveFile(HandoverRecordFile file)
    {
        context.Set<HandoverRecordFile>().Remove(file);
    }

    public void AddBeneficiary(Beneficiary beneficiary)
    {
        context.Set<Beneficiary>().Add(beneficiary);
    }

    public Task<Beneficiary?> FindBeneficiaryForUpdateAsync(Guid beneficiaryId, CancellationToken ct)
    {
        return context.Set<Beneficiary>().FirstOrDefaultAsync(b => b.Id == beneficiaryId, ct);
    }

    private IQueryable<HandoverRecord> QueryWithDetails()
    {
        return context.Set<HandoverRecord>()
            .Include(r => r.Lines)
            .Include(r => r.Signatures)
            .Include(r => r.Claims)
            .Include(r => r.Files)
            .AsSplitQuery();
    }
}
