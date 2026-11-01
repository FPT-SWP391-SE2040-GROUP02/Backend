using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Dùng context.Set&lt;T&gt;() thay vì DbSet trên AppDbContext để không sửa AppDbContext (D9, giống HandoverRepository).
/// </remarks>
public sealed class DeathCaseRepository : IDeathCaseRepository
{
    private readonly AppDbContext context;

    public DeathCaseRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<DeathCase?> FindByIdAsync(Guid caseId, CancellationToken ct)
    {
        return QueryWithDetails().FirstOrDefaultAsync(c => c.Id == caseId, ct);
    }

    public Task<DeathCaseAssignment?> FindAliveConfirmedAssignmentAsync(Guid caseId, Guid verifierUserId, CancellationToken ct)
    {
        string aliveConfirmed = DeathCaseAssignmentRules.EndReasonAliveConfirmed;
        return context.Set<DeathCaseAssignment>()
            .AsNoTracking()
            .Where(a => a.CaseId == caseId && a.VerifierUserId == verifierUserId && a.EndReason == aliveConfirmed)
            .OrderByDescending(a => a.EndedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<DeathCase?> FindOpenByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return QueryWithDetails()
            .FirstOrDefaultAsync(c => c.VaultId == vaultId && c.Status != DeathCaseStatus.Rejected, ct);
    }

    public async Task<List<ExecutorVaultRow>> FindExecutorVaultsAsync(Guid userId, CancellationToken ct)
    {
        List<ExecutorVaultRow> rows = await context.Set<Executor>()
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.Status == ExecutorStatus.Accepted)
            .OrderBy(e => e.AcceptedAt)
            .ThenBy(e => e.Id)
            .Select(e => new ExecutorVaultRow
            {
                VaultId = e.VaultId,
                OwnerFullName = e.Vault.Owner.FullName,
                PlanStatus = e.Vault.Plan.Status,
                OpenDeathCaseId = context.Set<DeathCase>()
                    .Where(c => c.VaultId == e.VaultId && c.Status != DeathCaseStatus.Rejected)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefault(),
                LastRejectedDeathCaseId = context.Set<DeathCase>()
                    .Where(c => c.VaultId == e.VaultId && c.Status == DeathCaseStatus.Rejected)
                    .OrderByDescending(c => c.CreatedAt)
                    .ThenByDescending(c => c.Id)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows;
    }

    public Task<DeathCase?> FindLatestRejectedByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<DeathCase>()
            .Where(c => c.VaultId == vaultId && c.Status == DeathCaseStatus.Rejected)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryAddAsync(DeathCase deathCase, CancellationToken ct)
    {
        context.Set<DeathCase>().Add(deathCase);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            List<object> entities = new List<object>();
            entities.Add(deathCase);
            foreach (DeathCaseVersion version in deathCase.Versions)
            {
                entities.Add(version);
            }
            DbExceptions.DetachAll(context, entities.ToArray());
            return false;
        }
    }

    public void AddVersion(DeathCaseVersion version)
    {
        context.Set<DeathCaseVersion>().Add(version);
    }

    public void AddFile(DeathCaseFile file)
    {
        context.Set<DeathCaseFile>().Add(file);
    }

    public void RemoveFile(DeathCaseFile file)
    {
        context.Set<DeathCaseFile>().Remove(file);
    }

    public void RemoveVersion(DeathCaseVersion version)
    {
        context.Set<DeathCaseVersion>().Remove(version);
    }

    public void RemoveCase(DeathCase deathCase)
    {
        context.Set<DeathCase>().Remove(deathCase);
    }

    public void AddFlag(DeathCaseFlag flag)
    {
        context.Set<DeathCaseFlag>().Add(flag);
    }

    public void AddNotice(DeathCaseNotice notice)
    {
        context.Set<DeathCaseNotice>().Add(notice);
    }

    public void AddAssignment(DeathCaseAssignment assignment)
    {
        context.Set<DeathCaseAssignment>().Add(assignment);
    }

    public Task<int> CountFilesUsingStorageKeyAsync(string storageKey, CancellationToken ct)
    {
        return context.Set<DeathCaseFile>().CountAsync(f => f.StorageKey == storageKey, ct);
    }

    public Task<bool> HasCheckInAtOrAfterAsync(Guid planId, DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        return context.Set<CheckInPeriod>()
            .AnyAsync(p => p.PlanId == planId && p.CheckedInAt != null && p.CheckedInAt >= cutoffUtc, ct);
    }

    public async Task<Guid?> FindVerifierCandidateAsync(
        List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct)
    {
        // SQL Server xếp NULL trước khi tăng dần: Verifier chưa từng được giao đứng đầu.
        // ROLE-03: loại thêm tài khoản có email trùng người nhận (danh sách rỗng thì điều kiện luôn đúng).
        Guid? candidateId = await context.Set<User>()
            .Where(u => u.Role == UserRole.Verifier
                && u.Status == UserStatus.Active
                && !excludedUserIds.Contains(u.Id)
                && !excludedEmails.Contains(u.Email))
            .Select(u => new
            {
                u.Id,
                LastAssignedAt = context.Set<DeathCaseAssignment>()
                    .Where(a => a.VerifierUserId == u.Id)
                    .Max(a => (DateTimeOffset?)a.AssignedAt),
            })
            .OrderBy(x => x.LastAssignedAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        return candidateId;
    }

    public Task<List<Guid>> FindPendingNoticeIdsAsync(int maxCount, CancellationToken ct)
    {
        return context.Set<DeathCaseNotice>()
            .Where(n => n.Status == DeathCaseNoticeStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Select(n => n.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public Task<DeathCaseNotice?> FindNoticeForJobAsync(Guid noticeId, CancellationToken ct)
    {
        return context.Set<DeathCaseNotice>().FirstOrDefaultAsync(n => n.Id == noticeId, ct);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        DbUpdateException? dbException = exception as DbUpdateException;
        if (dbException == null)
        {
            return false;
        }
        return DbExceptions.IsUniqueViolation(dbException);
    }

    public void DiscardPendingChanges()
    {
        context.ChangeTracker.Clear();
    }

    /// <summary>Hồ sơ kèm phiên bản, file, cờ, thông báo, quyết định và phân công còn hiệu lực; tách truy vấn tránh nhân dòng.</summary>
    private IQueryable<DeathCase> QueryWithDetails()
    {
        return context.Set<DeathCase>()
            .Include(c => c.Versions).ThenInclude(v => v.Files)
            .Include(c => c.Flags)
            .Include(c => c.Notices)
            .Include(c => c.Decisions)
            .Include(c => c.Assignments.Where(a => a.EndedAt == null))
            .AsSplitQuery();
    }
}
