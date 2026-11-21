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

public sealed class HoldRepository : IHoldRepository
{
    private readonly AppDbContext context;

    public HoldRepository(AppDbContext context)
    {
        this.context = context;
    }

    public void Add(Hold hold)
    {
        context.Set<Hold>().Add(hold);
    }

    public Task<Hold?> FindByIdAsync(Guid holdId, CancellationToken ct)
    {
        return context.Set<Hold>().FirstOrDefaultAsync(h => h.Id == holdId, ct);
    }

    public Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Hold>().AnyAsync(h => h.VaultId == vaultId && h.Status == HoldStatus.Active, ct);
    }

    public async Task<(List<Hold> Items, int TotalCount)> ListAsync(
        HoldStatus? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<Hold> query = context.Set<Hold>().AsNoTracking();
        if (status != null)
        {
            query = query.Where(h => h.Status == status.Value);
        }
        if (vaultId != null)
        {
            query = query.Where(h => h.VaultId == vaultId.Value);
        }
        int total = await query.CountAsync(ct);
        List<Hold> items = await query
            .OrderBy(h => h.ReviewAt)
            .ThenBy(h => h.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<Dictionary<HoldStatus, int>> CountByStatusAsync(Guid? vaultId, CancellationToken ct)
    {
        IQueryable<Hold> query = context.Set<Hold>().AsNoTracking();
        if (vaultId != null)
        {
            query = query.Where(h => h.VaultId == vaultId.Value);
        }
        var rows = await query.GroupBy(h => h.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    public Task<int> CountActiveAsync(CancellationToken ct)
    {
        return context.Set<Hold>().CountAsync(h => h.Status == HoldStatus.Active, ct);
    }

    public Task<int> CountReviewOverdueAsync(DateTimeOffset now, CancellationToken ct)
    {
        return context.Set<Hold>().CountAsync(h => h.Status == HoldStatus.Active && h.ReviewAt <= now, ct);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return SqlErrors.IsUniqueViolation(exception);
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

    public async Task<List<(Guid CaseId, HandoverRecordStatus Status, DateTimeOffset DueAt)>> ListOpenAssignmentsAsync(
        Guid verifierUserId, CancellationToken ct)
    {
        var rows = await context.Set<HandoverRecord>().AsNoTracking()
            .Where(r => r.VerifierUserId == verifierUserId && r.Status != HandoverRecordStatus.Completed)
            .OrderBy(r => r.RequestedAt)
            .Select(r => new { r.Id, r.Status, r.RequestedAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Status, r.RequestedAt)).ToList();
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

    public Task<int> CountCasesWithoutVerifierAsync(CancellationToken ct)
    {
        return context.Set<HandoverRecord>().CountAsync(
            r => r.Status != HandoverRecordStatus.Completed
                && !context.Set<User>().Any(u => u.Id == r.VerifierUserId && u.Status == UserStatus.Active), ct);
    }
}
