using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class HandoverDocumentRepository : IHandoverDocumentRepository
{
    private readonly AppDbContext context;

    public HandoverDocumentRepository(AppDbContext context)
    {
        this.context = context;
    }

    public void Add(HandoverDocument document)
    {
        context.Set<HandoverDocument>().Add(document);
    }

    public Task<HandoverDocument?> FindByIdAsync(Guid documentId, CancellationToken ct)
    {
        return context.Set<HandoverDocument>()
            .Include(d => d.Turn)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);
    }

    public Task<HandoverDocument?> FindByTurnIdAsync(Guid turnId, CancellationToken ct)
    {
        return context.Set<HandoverDocument>()
            .Include(d => d.Turn)
            .FirstOrDefaultAsync(d => d.TurnId == turnId, ct);
    }

    public Task<List<HandoverDocument>> ListByRecipientAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<HandoverDocument>()
            .AsNoTracking()
            .Include(d => d.Turn)
            .Where(d => d.Turn.ClaimantUserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);
    }

    public Task<List<HandoverDocument>> ListByVaultIdsAsync(List<Guid> vaultIds, CancellationToken ct)
    {
        return context.Set<HandoverDocument>()
            .AsNoTracking()
            .Include(d => d.Turn)
            .Where(d => vaultIds.Contains(d.VaultId))
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);
    }

    public Task<List<HandoverDocument>> ListForVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        return context.Set<HandoverDocument>()
            .AsNoTracking()
            .Include(d => d.Turn)
            .Where(d => context.Set<DeathCaseDecision>().Any(x => x.Kind == DeathCaseDecisionKind.Approved
                && x.VerifierUserId == verifierUserId
                && context.Set<DeathCase>().Any(c => c.Id == x.CaseId && c.VaultId == d.VaultId)))
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);
    }

    public async Task<VerifierDocumentContext> LoadVerifierContextAsync(
        Guid verifierUserId, List<Guid> vaultIds, List<Guid> turnIds, CancellationToken ct)
    {
        VerifierDocumentContext result = new VerifierDocumentContext();
        if (vaultIds.Count == 0)
        {
            return result;
        }

        // Ngày Người xét duyệt này duyệt hồ sơ báo tin của từng kho (lấy lần gần nhất).
        var approvals = await context.Set<DeathCaseDecision>()
            .AsNoTracking()
            .Where(x => x.Kind == DeathCaseDecisionKind.Approved && x.VerifierUserId == verifierUserId)
            .Join(context.Set<DeathCase>().Where(c => vaultIds.Contains(c.VaultId)),
                x => x.CaseId, c => c.Id, (x, c) => new { c.VaultId, x.DecidedAt })
            .ToListAsync(ct);
        foreach (var approval in approvals)
        {
            DateTimeOffset existing;
            if (!result.CaseApprovedAtByVault.TryGetValue(approval.VaultId, out existing) || approval.DecidedAt > existing)
            {
                result.CaseApprovedAtByVault[approval.VaultId] = approval.DecidedAt;
            }
        }

        // Tổng người nhận của kho: mỗi chỉ định (gói + người nhận) có lượt bàn giao tính một.
        var turnRows = await context.Set<HandoverTurn>()
            .AsNoTracking()
            .Where(t => vaultIds.Contains(t.VaultId)
                && t.Status != HandoverTurnStatus.Cancelled
                && t.Status != HandoverTurnStatus.Lapsed)
            .Select(t => new { t.VaultId, t.DesignationId })
            .Distinct()
            .ToListAsync(ct);
        foreach (var row in turnRows)
        {
            int count;
            result.RecipientTotalByVault.TryGetValue(row.VaultId, out count);
            result.RecipientTotalByVault[row.VaultId] = count + 1;
        }

        // Thời điểm nộp và số lần nộp của từng lượt.
        var submissionRows = await context.Set<HandoverSubmission>()
            .AsNoTracking()
            .Where(s => turnIds.Contains(s.TurnId))
            .Select(s => new { s.Id, s.TurnId, s.CreatedAt })
            .ToListAsync(ct);
        foreach (var row in submissionRows)
        {
            result.SubmittedAtBySubmission[row.Id] = row.CreatedAt;
            int count;
            result.SubmissionCountByTurn.TryGetValue(row.TurnId, out count);
            result.SubmissionCountByTurn[row.TurnId] = count + 1;
        }
        return result;
    }
}
