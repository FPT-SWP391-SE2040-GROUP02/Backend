using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class DevToolsRepository(AppDbContext context) : IDevToolsRepository
{
    public Task<List<Vault>> ListVaultsAsync(int maxCount, CancellationToken ct)
    {
        return context.Set<Vault>()
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Plan)
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public Task<Vault?> FindVaultWithEntitlementAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Vault>()
            .Include(v => v.Plan)
            .Include(v => v.Entitlement)
            .FirstOrDefaultAsync(v => v.Id == vaultId, ct);
    }

    public Task<List<PaymentOrder>> ListPendingPaymentOrdersAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<PaymentOrder>()
            .Where(o => o.VaultId == vaultId && o.Status == PaymentOrderStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToListAsync(ct);
    }

    public async Task<AuditAction?> FindLatestClockActionAsync(Guid vaultId, CancellationToken ct)
    {
        AuditLog? log = await context.Set<AuditLog>()
            .AsNoTracking()
            .Where(l => l.VaultId == vaultId
                && (l.Action == AuditAction.DevTimeTravel || l.Action == AuditAction.DevClockReset))
            .OrderByDescending(l => l.At)
            .ThenByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);
        if (log == null)
        {
            return null;
        }
        return log.Action;
    }

    public Task<List<EkycVerification>> ListEkycVerificationsAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<EkycVerification>()
            .Where(v => v.UserId == userId)
            .OrderBy(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .ToListAsync(ct);
    }

    public Task<List<EkycConsent>> ListEkycConsentsAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<EkycConsent>()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.ConsentedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<OwnerIdentity?> FindOwnerIdentityAsync(Guid userId, CancellationToken ct)
    {
        return context.Set<OwnerIdentity>().FirstOrDefaultAsync(i => i.UserId == userId, ct);
    }

    public void RemoveEkycData(List<EkycVerification> verifications, List<EkycConsent> consents, OwnerIdentity? identity)
    {
        context.Set<EkycVerification>().RemoveRange(verifications);
        context.Set<EkycConsent>().RemoveRange(consents);
        if (identity != null)
        {
            context.Set<OwnerIdentity>().Remove(identity);
        }
    }

    public async Task<Guid?> FindVaultIdByOwnerAsync(Guid ownerId, CancellationToken ct)
    {
        Vault? vault = await context.Set<Vault>()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.OwnerId == ownerId, ct);
        if (vault == null)
        {
            return null;
        }
        return vault.Id;
    }

    public async Task<List<string>> ListVaultStorageKeysAsync(Guid vaultId, CancellationToken ct)
    {
        List<string> keys = new List<string>();

        List<string?> assetKeys = await context.Set<Asset>()
            .AsNoTracking()
            .Where(a => a.VaultId == vaultId)
            .Select(a => (string?)a.StorageKey)
            .ToListAsync(ct);
        AddKeys(keys, assetKeys);

        var caseIds = context.Set<DeathCase>().Where(c => c.VaultId == vaultId).Select(c => c.Id);
        var versionIds = context.Set<DeathCaseVersion>().Where(v => caseIds.Contains(v.CaseId)).Select(v => v.Id);
        List<string?> fileKeys = await context.Set<DeathCaseFile>()
            .AsNoTracking()
            .Where(f => versionIds.Contains(f.VersionId))
            .Select(f => (string?)f.StorageKey)
            .ToListAsync(ct);
        AddKeys(keys, fileKeys);

        return keys;
    }

    public async Task DeleteVaultAsync(Guid vaultId, CancellationToken ct)
    {
        // Bàn giao: nhật ký tải → quyền tải → tài sản đã ghim → lượt.
        var turnIds = context.Set<HandoverTurn>().Where(t => t.VaultId == vaultId).Select(t => t.Id);
        var grantIds = context.Set<HandoverGrant>().Where(g => turnIds.Contains(g.TurnId)).Select(g => g.Id);
        await context.Set<HandoverAccessLog>().Where(l => grantIds.Contains(l.GrantId)).ExecuteDeleteAsync(ct);
        await context.Set<HandoverGrant>().Where(g => turnIds.Contains(g.TurnId)).ExecuteDeleteAsync(ct);
        await context.Set<HandoverSnapshotItem>().Where(i => turnIds.Contains(i.TurnId)).ExecuteDeleteAsync(ct);
        await context.Set<HandoverTurn>().Where(t => t.VaultId == vaultId).ExecuteDeleteAsync(ct);

        // Hồ sơ chứng tử: quyết định → phân công → cờ → trang giấy → bản khai → thông báo → lời báo còn sống → hồ sơ.
        var caseIds = context.Set<DeathCase>().Where(c => c.VaultId == vaultId).Select(c => c.Id);
        var versionIds = context.Set<DeathCaseVersion>().Where(v => caseIds.Contains(v.CaseId)).Select(v => v.Id);
        await context.Set<DeathCaseDecision>().Where(d => caseIds.Contains(d.CaseId)).ExecuteDeleteAsync(ct);
        await context.Set<DeathCaseAssignment>().Where(a => caseIds.Contains(a.CaseId)).ExecuteDeleteAsync(ct);
        await context.Set<DeathCaseFlag>().Where(f => caseIds.Contains(f.CaseId)).ExecuteDeleteAsync(ct);
        await context.Set<DeathCaseFile>().Where(f => versionIds.Contains(f.VersionId)).ExecuteDeleteAsync(ct);
        await context.Set<DeathCaseVersion>().Where(v => caseIds.Contains(v.CaseId)).ExecuteDeleteAsync(ct);
        // Hồ sơ trỏ vào hồ sơ trước của cùng kho: gỡ liên kết rồi mới xóa được.
        await context.Set<DeathCase>()
            .Where(c => c.VaultId == vaultId && c.PreviousCaseId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PreviousCaseId, (Guid?)null), ct);
        await context.Set<DeathCase>().Where(c => c.VaultId == vaultId).ExecuteDeleteAsync(ct);

        // Nội dung kho: chỉ định, tài sản trong gói, Executor (trỏ vào người nhận) → gói, tài sản, người nhận.
        var containerIds = context.Set<Container>().Where(c => c.VaultId == vaultId).Select(c => c.Id);
        await context.Set<Designation>().Where(d => containerIds.Contains(d.ContainerId)).ExecuteDeleteAsync(ct);
        await context.Set<ContainerAsset>().Where(a => containerIds.Contains(a.ContainerId)).ExecuteDeleteAsync(ct);
        await context.Set<Executor>().Where(e => e.VaultId == vaultId).ExecuteDeleteAsync(ct);
        // Mã mở gói do server tạo khi kích hoạt kế hoạch: phải xóa trước kho vì khóa ngoại tới Vaults.
        await context.Set<ExecutorKeyShard>().Where(s => s.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<Container>().Where(c => c.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<Asset>().Where(a => a.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<Beneficiary>().Where(b => b.VaultId == vaultId).ExecuteDeleteAsync(ct);

        // Vận hành và thanh toán.
        await context.Set<StorageReservation>().Where(r => r.VaultId == vaultId).ExecuteDeleteAsync(ct);
        var orderIds = context.Set<PaymentOrder>().Where(o => o.VaultId == vaultId).Select(o => o.Id);
        await context.Set<PaymentTransaction>()
            .Where(t => t.PaymentOrderId != null && orderIds.Contains(t.PaymentOrderId.Value))
            .ExecuteDeleteAsync(ct);
        await context.Set<PaymentOrder>().Where(o => o.VaultId == vaultId).ExecuteDeleteAsync(ct);

        // Cuối cùng: PIN, quyền dịch vụ, kế hoạch, nhật ký của kho, kho.
        await context.Set<VaultPin>().Where(p => p.VaultId == vaultId).ExecuteDeleteAsync(ct);
        // Các phiên bản di chúc số đã ký: có khóa ngoại tới Vaults nên phải xóa trước kho.
        await context.Set<WillVersion>().Where(w => w.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<VaultEntitlement>().Where(e => e.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<HandoverPlan>().Where(p => p.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<AuditLog>().Where(l => l.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<Vault>().Where(v => v.Id == vaultId).ExecuteDeleteAsync(ct);
    }

    private static void AddKeys(List<string> keys, List<string?> candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && !keys.Contains(candidate))
            {
                keys.Add(candidate);
            }
        }
    }
}
