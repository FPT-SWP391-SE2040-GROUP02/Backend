using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class HandoverSubmissionRepository : IHandoverSubmissionRepository
{
    private readonly AppDbContext context;

    public HandoverSubmissionRepository(AppDbContext context)
    {
        this.context = context;
    }

    public void Add(HandoverSubmission submission)
    {
        context.Set<HandoverSubmission>().Add(submission);
    }

    public Task<HandoverSubmission?> FindLatestByTurnAsync(Guid turnId, CancellationToken ct)
    {
        return context.Set<HandoverSubmission>()
            .Where(s => s.TurnId == turnId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<HandoverSubmission?> FindByIdWithTurnAsync(Guid submissionId, CancellationToken ct)
    {
        return context.Set<HandoverSubmission>()
            .Include(s => s.Turn)
            .ThenInclude(t => t.Items)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);
    }

    public Task<List<HandoverSubmission>> ListPendingForVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        return context.Set<HandoverSubmission>()
            .Include(s => s.Turn)
            .Where(s => s.Status == HandoverSubmissionStatus.Submitted
                && context.Set<DeathCaseDecision>().Any(d => d.Kind == DeathCaseDecisionKind.Approved
                    && d.VerifierUserId == verifierUserId
                    && context.Set<DeathCase>().Any(c => c.Id == d.CaseId && c.VaultId == s.Turn.VaultId)))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<List<HandoverSubmission>> ListByTurnIdsAsync(List<Guid> turnIds, CancellationToken ct)
    {
        return context.Set<HandoverSubmission>().Where(s => turnIds.Contains(s.TurnId)).ToListAsync(ct);
    }

    public async Task<(string FullName, string? LawyerCardNumber)?> FindApprovingVerifierAsync(Guid vaultId, CancellationToken ct)
    {
        var row = await context.Set<DeathCaseDecision>()
            .AsNoTracking()
            .Where(d => d.Kind == DeathCaseDecisionKind.Approved
                && context.Set<DeathCase>().Any(c => c.Id == d.CaseId && c.VaultId == vaultId))
            .OrderByDescending(d => d.DecidedAt)
            .Join(context.Set<User>(), d => d.VerifierUserId, u => u.Id, (d, u) => new { u.FullName, u.LawyerCardNumber })
            .FirstOrDefaultAsync(ct);
        if (row == null)
        {
            return null;
        }
        return (row.FullName, row.LawyerCardNumber);
    }

    public Task<bool> IsApprovingVerifierAsync(Guid verifierUserId, Guid vaultId, CancellationToken ct)
    {
        return context.Set<DeathCaseDecision>()
            .AnyAsync(d => d.Kind == DeathCaseDecisionKind.Approved
                && d.VerifierUserId == verifierUserId
                && context.Set<DeathCase>().Any(c => c.Id == d.CaseId && c.VaultId == vaultId), ct);
    }
}
