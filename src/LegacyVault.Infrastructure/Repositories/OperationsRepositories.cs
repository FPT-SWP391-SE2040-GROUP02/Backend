using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

internal static class SqlErrors
{
    /// <summary>SQL Server: 2601 = trùng chỉ mục unique, 2627 = vi phạm ràng buộc unique.</summary>
    public static bool IsUniqueViolation(Exception exception)
    {
        if (exception is not DbUpdateException)
        {
            return false;
        }
        Exception? inner = exception.InnerException;
        while (inner != null)
        {
            if (inner is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }
}

public sealed class AdminUserRepository : IAdminUserRepository
{
    private readonly AppDbContext context;

    public AdminUserRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<(List<User> Items, int TotalCount)> ListAsync(
        string? search, UserRole? role, UserStatus? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<User> query = context.Set<User>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }
        if (role != null)
        {
            query = query.Where(u => u.Role == role.Value);
        }
        if (status != null)
        {
            query = query.Where(u => u.Status == status.Value);
        }
        int total = await query.CountAsync(ct);
        List<User> items = await query
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<Dictionary<UserRole, int>> CountByRoleAsync(string? search, UserStatus? status, CancellationToken ct)
    {
        IQueryable<User> query = context.Set<User>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }
        if (status != null)
        {
            query = query.Where(u => u.Status == status.Value);
        }
        var rows = await query.GroupBy(u => u.Role).Select(g => new { Role = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Role, r => r.Count);
    }

    public Task<List<User>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        return context.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken ct)
    {
        return context.Set<User>().CountAsync(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active, ct);
    }

    public async Task<List<(Guid RecordId, HandoverRecordStatus Status, DateTimeOffset RequestedAt)>> ListOpenRecordsAsync(
        Guid verifierUserId, CancellationToken ct)
    {
        var rows = await context.Set<HandoverRecord>().AsNoTracking()
            .Where(r => r.VerifierUserId == verifierUserId
                && r.Status != HandoverRecordStatus.Completed
                && r.Status != HandoverRecordStatus.Cancelled)
            .OrderBy(r => r.RequestedAt)
            .Select(r => new { r.Id, r.Status, r.RequestedAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Status, r.RequestedAt)).ToList();
    }

    public async Task<List<(Guid ReportId, HandoverClaimStatus Status, DateTimeOffset CreatedAt)>> ListPendingReportsAsync(
        Guid verifierUserId, CancellationToken ct)
    {
        var rows = await context.Set<HandoverReport>().AsNoTracking()
            .Where(r => r.VerifierUserId == verifierUserId
                && (r.Status == HandoverClaimStatus.Submitted || r.Status == HandoverClaimStatus.SupplementRequested))
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Status, r.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Status, r.CreatedAt)).ToList();
    }

    public async Task<List<(Guid RecordId, HandoverClaimStatus Status, DateTimeOffset ReviewedAt)>> ListRecentClaimReviewsAsync(
        Guid verifierUserId, int take, CancellationToken ct)
    {
        var rows = await context.Set<HandoverClaim>().AsNoTracking()
            .Where(c => c.ReviewedByUserId == verifierUserId
                && c.ReviewedAt != null
                && (c.Status == HandoverClaimStatus.Accepted
                    || c.Status == HandoverClaimStatus.Rejected
                    || c.Status == HandoverClaimStatus.SupplementRequested))
            .OrderByDescending(c => c.ReviewedAt)
            .Take(take)
            .Select(c => new { c.RecordId, c.Status, ReviewedAt = c.ReviewedAt!.Value })
            .ToListAsync(ct);
        return rows.Select(r => (r.RecordId, r.Status, r.ReviewedAt)).ToList();
    }

    public async Task<List<(Guid ReportId, HandoverClaimStatus Status, DateTimeOffset ReviewedAt)>> ListRecentReportReviewsAsync(
        Guid verifierUserId, int take, CancellationToken ct)
    {
        var rows = await context.Set<HandoverReport>().AsNoTracking()
            .Where(r => r.VerifierUserId == verifierUserId
                && r.ReviewedAt != null
                && (r.Status == HandoverClaimStatus.Accepted
                    || r.Status == HandoverClaimStatus.Rejected
                    || r.Status == HandoverClaimStatus.SupplementRequested))
            .OrderByDescending(r => r.ReviewedAt)
            .Take(take)
            .Select(r => new { r.Id, r.Status, ReviewedAt = r.ReviewedAt!.Value })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Status, r.ReviewedAt)).ToList();
    }

    public async Task<List<(Guid VaultId, string OwnerFullName, VaultStatus VaultStatus, PlanStatus PlanStatus, HandoverRecordStatus? RecordStatus)>>
        ListHeldVaultsAsync(Guid executorUserId, CancellationToken ct)
    {
        var rows = await context.Set<Executor>().AsNoTracking()
            .Where(e => e.UserId == executorUserId && e.Status == ExecutorStatus.Accepted)
            .OrderByDescending(e => e.AcceptedAt)
            .Select(e => new
            {
                e.VaultId,
                OwnerFullName = e.Vault.Owner.FullName,
                VaultStatus = e.Vault.Status,
                PlanStatus = e.Vault.Plan.Status,
                RecordStatus = context.Set<HandoverRecord>()
                    .Where(r => r.VaultId == e.VaultId && r.Status != HandoverRecordStatus.Cancelled)
                    .OrderByDescending(r => r.RequestedAt)
                    .Select(r => (HandoverRecordStatus?)r.Status)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);
        return rows.Select(r => (r.VaultId, r.OwnerFullName, r.VaultStatus, r.PlanStatus, r.RecordStatus)).ToList();
    }

    public Task<List<AuditLog>> ListRecentAuditByActorAsync(Guid userId, int take, CancellationToken ct)
    {
        return context.Set<AuditLog>().AsNoTracking()
            .Where(l => l.ActorUserId == userId)
            .OrderByDescending(l => l.At)
            .ThenByDescending(l => l.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<List<(PlanStatus Status, int Count)>> CountPlansByStatusAsync(CancellationToken ct)
    {
        var rows = await context.Set<HandoverPlan>().AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => (r.Status, r.Count)).ToList();
    }

    public async Task<List<(Guid VerifierUserId, int Open, int Overdue)>> CountVerifierLoadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        // Biên bản không có hạn chót nên không có biên bản trễ hạn.
        var rows = await context.Set<HandoverRecord>().AsNoTracking()
            .Where(r => r.Status != HandoverRecordStatus.Completed)
            .GroupBy(r => r.VerifierUserId)
            .Select(g => new { VerifierUserId = g.Key, Open = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => (r.VerifierUserId, r.Open, 0)).ToList();
    }

    public async Task<List<(AuditLog Log, string? ActorFullName)>> ListRecentAuditAsync(int take, CancellationToken ct)
    {
        var rows = await (
            from log in context.Set<AuditLog>().AsNoTracking()
            join user in context.Set<User>().AsNoTracking() on log.ActorUserId equals user.Id into actors
            from actor in actors.DefaultIfEmpty()
            orderby log.At descending, log.Id descending
            select new { Log = log, Name = actor == null ? (string?)null : actor.FullName })
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => (r.Log, (string?)r.Name)).ToList();
    }

    public async Task<int> CountCasesWithoutVerifierAsync(CancellationToken ct)
    {
        int records = await context.Set<HandoverRecord>().CountAsync(
            r => r.Status != HandoverRecordStatus.Completed
                && r.Status != HandoverRecordStatus.Cancelled
                && !context.Set<User>().Any(u => u.Id == r.VerifierUserId && u.Status == UserStatus.Active), ct);
        int reports = await context.Set<HandoverReport>().CountAsync(
            r => (r.Status == HandoverClaimStatus.Submitted || r.Status == HandoverClaimStatus.SupplementRequested)
                && !context.Set<User>().Any(u => u.Id == r.VerifierUserId && u.Status == UserStatus.Active), ct);
        return records + reports;
    }
}
