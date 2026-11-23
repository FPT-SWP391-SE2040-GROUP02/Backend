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

        // Biên bản bàn giao: chữ ký và dòng xóa theo biên bản (cascade).
        await context.Set<HandoverRecord>().Where(r => r.VaultId == vaultId).ExecuteDeleteAsync(ct);

        // Nội dung kho: dòng gán tài sản cho người nhận, Executor (trỏ vào người nhận) → tài sản, người nhận.
        await context.Set<AssetAssignment>().Where(a => a.Asset.VaultId == vaultId).ExecuteDeleteAsync(ct);
        await context.Set<Executor>().Where(e => e.VaultId == vaultId).ExecuteDeleteAsync(ct);
        // Mã mở gói do server tạo khi kích hoạt kế hoạch: phải xóa trước kho vì khóa ngoại tới Vaults.
        await context.Set<ExecutorKeyShard>().Where(s => s.VaultId == vaultId).ExecuteDeleteAsync(ct);
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
