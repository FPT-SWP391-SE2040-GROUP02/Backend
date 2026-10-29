using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Dùng context.Set&lt;T&gt;() thay vì DbSet trên AppDbContext để không sửa AppDbContext (giống DeathCaseRepository).
/// </remarks>
public sealed class DeathCaseReviewRepository : IDeathCaseReviewRepository
{
    private readonly AppDbContext context;

    public DeathCaseReviewRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<(List<VerifierCaseRow> Items, int TotalCount)> FindAssignedPageAsync(
        Guid verifierUserId, string sortBy, bool isAscending, int pageIndex, int pageSize, CancellationToken ct)
    {
        // V1 + pool: hồ sơ có phân công đang hiệu lực của Verifier này, HOẶC hồ sơ đang chờ xét duyệt chưa có phân công hiệu lực.
        IQueryable<VerifierCaseRow> query = context.Set<DeathCase>()
            .AsNoTracking()
            .Where(c => c.Assignments.Any(a => a.VerifierUserId == verifierUserId && a.EndedAt == null)
                || (!c.Assignments.Any(a => a.EndedAt == null) && (c.Status == DeathCaseStatus.UnderReview || c.Status == DeathCaseStatus.SupplementRequired || c.Status == DeathCaseStatus.Suspended)))
            .Select(c => new VerifierCaseRow
            {
                Id = c.Id,
                Status = c.Status,
                ReviewVersionNo = c.ReviewVersionNo,
                OwnerCitizenIdLast4 = c.Versions
                    .Where(v => v.VersionNo == c.ReviewVersionNo)
                    .Select(v => v.OwnerCitizenIdLast4)
                    .FirstOrDefault(),
                DateOfDeath = c.Versions
                    .Where(v => v.VersionNo == c.ReviewVersionNo)
                    .Select(v => v.DateOfDeath)
                    .FirstOrDefault(),
                SubmittedAt = c.Versions
                    .Where(v => v.VersionNo == c.ReviewVersionNo)
                    .Select(v => v.SubmittedAt)
                    .FirstOrDefault(),
                AssignedAt = c.Assignments
                    .Where(a => a.VerifierUserId == verifierUserId && a.EndedAt == null)
                    .Select(a => (DateTimeOffset?)a.AssignedAt)
                    .FirstOrDefault()
                    ?? c.Assignments.Where(a => a.EndedAt == null).Select(a => (DateTimeOffset?)a.AssignedAt).FirstOrDefault()
                    ?? c.Versions.Where(v => v.VersionNo == c.ReviewVersionNo).Select(v => (DateTimeOffset?)v.SubmittedAt).FirstOrDefault()
                    ?? c.CreatedAt,
                DueAt = c.Assignments
                    .Where(a => a.VerifierUserId == verifierUserId && a.EndedAt == null)
                    .Select(a => (DateTimeOffset?)a.DueAt)
                    .FirstOrDefault()
                    ?? c.Assignments.Where(a => a.EndedAt == null).Select(a => (DateTimeOffset?)a.DueAt).FirstOrDefault()
                    ?? c.ObjectionEndsAt
                    ?? c.CreatedAt.AddDays(7),
                ObjectionEndsAt = c.ObjectionEndsAt,
                SupplementOverdueAt = c.SupplementOverdueAt,
                UnreviewedFlagCount = c.Flags.Count(f => f.ReviewedAt == null
                    && c.Versions.Any(v => v.Id == f.VersionId && v.VersionNo == c.ReviewVersionNo)),
                ClosedAt = c.ClosedAt,
                ExecutorFullName = context.Set<User>()
                    .Where(u => u.Id == c.Versions
                        .Where(v => v.VersionNo == c.ReviewVersionNo)
                        .Select(v => v.SubmittedByUserId)
                        .FirstOrDefault())
                    .Select(u => u.FullName)
                    .FirstOrDefault(),
                ExecutorEmail = context.Set<User>()
                    .Where(u => u.Id == c.Versions
                        .Where(v => v.VersionNo == c.ReviewVersionNo)
                        .Select(v => v.SubmittedByUserId)
                        .FirstOrDefault())
                    .Select(u => u.Email)
                    .FirstOrDefault(),
            });

        int totalCount = await query.CountAsync(ct);

        // Hồ sơ còn mở (ClosedAt null) lên trước; ThenBy(Id) giữ thứ tự cố định khi chuyển trang.
        IOrderedQueryable<VerifierCaseRow> ordered = query.OrderByDescending(r => r.ClosedAt == null);
        if (sortBy == "assignedAt")
        {
            if (isAscending)
            {
                ordered = ordered.ThenBy(r => r.AssignedAt).ThenBy(r => r.Id);
            }
            else
            {
                ordered = ordered.ThenByDescending(r => r.AssignedAt).ThenByDescending(r => r.Id);
            }
        }
        else
        {
            if (isAscending)
            {
                ordered = ordered.ThenBy(r => r.DueAt).ThenBy(r => r.Id);
            }
            else
            {
                ordered = ordered.ThenByDescending(r => r.DueAt).ThenByDescending(r => r.Id);
            }
        }

        List<VerifierCaseRow> items = await ordered
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<DeathCaseFile?> FindSubmittedFileAsync(Guid caseId, Guid fileId, CancellationToken ct)
    {
        return context.Set<DeathCaseFile>()
            .AsNoTracking()
            .Where(f => f.Id == fileId
                && context.Set<DeathCaseVersion>()
                    .Any(v => v.Id == f.VersionId && v.CaseId == caseId && v.SubmittedAt != null))
            .FirstOrDefaultAsync(ct);
    }

    public Task<PreviousCaseRow?> FindPreviousCaseAsync(Guid previousCaseId, CancellationToken ct)
    {
        return context.Set<DeathCase>()
            .AsNoTracking()
            .Where(c => c.Id == previousCaseId)
            .Select(c => new PreviousCaseRow
            {
                Id = c.Id,
                ClosedAt = c.ClosedAt,
                RejectReason = c.Decisions
                    .Where(d => d.Kind == DeathCaseDecisionKind.Rejected)
                    .Select(d => d.Reason)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
    }

    public void AddDecision(DeathCaseDecision decision)
    {
        context.Set<DeathCaseDecision>().Add(decision);
    }
}
