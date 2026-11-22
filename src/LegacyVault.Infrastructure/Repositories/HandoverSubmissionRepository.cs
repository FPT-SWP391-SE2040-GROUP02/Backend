using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <summary>Đọc và ghi hồ sơ người nhận (EF Core).</summary>
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
        // Chỉ hồ sơ của lượt thuộc biên bản đang ở bước kiểm danh tính, được giao cho luật sư này.
        return context.Set<HandoverSubmission>()
            .Include(s => s.Turn)
            .Where(s => s.Status == HandoverSubmissionStatus.Submitted
                && context.Set<HandoverRecord>().Any(r => r.Id == s.Turn.RecordId
                    && r.VerifierUserId == verifierUserId
                    && r.Status == HandoverRecordStatus.IdentityCheck))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<List<HandoverSubmission>> ListByTurnIdsAsync(List<Guid> turnIds, CancellationToken ct)
    {
        return context.Set<HandoverSubmission>().Where(s => turnIds.Contains(s.TurnId)).ToListAsync(ct);
    }

    public async Task<ApprovingVerifierRow?> FindApprovingVerifierAsync(Guid vaultId, CancellationToken ct)
    {
        ApprovingVerifierRow? row = await context.Set<HandoverRecord>()
            .AsNoTracking()
            .Where(r => r.VaultId == vaultId)
            .Join(context.Set<User>(), r => r.VerifierUserId, u => u.Id,
                (r, u) => new ApprovingVerifierRow { FullName = u.FullName, LawyerCardNumber = u.LawyerCardNumber })
            .FirstOrDefaultAsync(ct);
        return row;
    }

    public async Task<Guid?> FindApprovingVerifierUserIdAsync(Guid vaultId, CancellationToken ct)
    {
        Guid? verifierId = await context.Set<HandoverRecord>()
            .AsNoTracking()
            .Where(r => r.VaultId == vaultId)
            .Select(r => (Guid?)r.VerifierUserId)
            .FirstOrDefaultAsync(ct);
        return verifierId;
    }

    public Task<bool> IsApprovingVerifierAsync(Guid verifierUserId, Guid vaultId, CancellationToken ct)
    {
        return context.Set<HandoverRecord>()
            .AnyAsync(r => r.VaultId == vaultId && r.VerifierUserId == verifierUserId, ct);
    }
}
