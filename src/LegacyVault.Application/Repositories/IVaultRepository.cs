using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Vaults (kèm HandoverPlans và VaultEntitlements).</summary>
public interface IVaultRepository
{
    /// <summary>Tìm theo Id, kèm Plan và Entitlement (có theo dõi thay đổi). Không thấy → null.</summary>
    Task<Vault?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Kho duy nhất của một Owner (SETUP-12), kèm Plan và Entitlement. Chưa có → null.</summary>
    Task<Vault?> FindByOwnerIdAsync(Guid ownerId, CancellationToken ct);

    /// <summary>
    /// Thêm kho mới cùng Plan và Entitlement rồi lưu ngay trong một transaction (STATE-02).
    /// Trả false khi DB từ chối vì Owner đã có kho (unique OwnerId): request khác vừa tạo trước
    /// (hai tab, bấm lặp, retry — AC-89). Khi đó kho truyền vào không được lưu.
    /// </summary>
    Task<bool> TryAddAsync(Vault vault, CancellationToken ct);

    /// <summary>Tổng byte đã dùng của kho (QUOTA-01). Chưa có module tài sản thì cài đặt trả 0.</summary>
    Task<long> SumUsedBytesAsync(Guid vaultId, CancellationToken ct);
}
