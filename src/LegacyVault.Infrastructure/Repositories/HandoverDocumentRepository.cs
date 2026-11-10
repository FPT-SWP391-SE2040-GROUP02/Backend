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
}
