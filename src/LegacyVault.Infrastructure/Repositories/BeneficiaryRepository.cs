using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ là truy cập dữ liệu theo IBeneficiaryRepository; luật nghiệp vụ ở BeneficiaryService.</remarks>
public sealed class BeneficiaryRepository : IBeneficiaryRepository
{
    private readonly AppDbContext context;

    public BeneficiaryRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<Beneficiary?> FindByIdAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        return context.Beneficiaries
            .FirstOrDefaultAsync(b => b.Id == beneficiaryId && b.VaultId == vaultId, ct);
    }

    public Task<List<Beneficiary>> ListByOwnerAndCitizenIdLast4Async(Guid ownerId, string last4, CancellationToken ct)
    {
        return context.Beneficiaries
            .Where(b => b.Vault.OwnerId == ownerId && b.CitizenIdLast4 == last4)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);
    }

    public Task<List<Beneficiary>> ListByCitizenIdLast4Async(Guid vaultId, string last4, CancellationToken ct)
    {
        return context.Beneficiaries
            .Where(b => b.VaultId == vaultId && b.CitizenIdLast4 == last4)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);
    }

    public async Task<(List<Beneficiary> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, string? last4Filter, int pageIndex, int pageSize, bool isAscending, CancellationToken ct)
    {
        IQueryable<Beneficiary> query = context.Beneficiaries.AsNoTracking().Where(b => b.VaultId == vaultId);
        if (!string.IsNullOrEmpty(last4Filter))
        {
            query = query.Where(b => b.CitizenIdLast4.EndsWith(last4Filter));
        }

        int totalCount = await query.CountAsync(ct);

        // ThenBy(Id): khi trùng thời điểm tạo vẫn có thứ tự cố định, chuyển trang không lặp/sót.
        if (isAscending)
        {
            query = query.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id);
        }
        else
        {
            query = query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id);
        }

        List<Beneficiary> items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<int> CountByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Beneficiaries.CountAsync(b => b.VaultId == vaultId, ct);
    }

    public void Add(Beneficiary beneficiary)
    {
        context.Beneficiaries.Add(beneficiary);
    }

    public void Remove(Beneficiary beneficiary)
    {
        context.Beneficiaries.Remove(beneficiary);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        DbUpdateException? update = exception as DbUpdateException;
        if (update == null)
        {
            return false;
        }
        return DbExceptions.IsUniqueViolation(update);
    }

    public void DiscardPendingChanges()
    {
        context.ChangeTracker.Clear();
    }
}
