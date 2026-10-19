using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng OwnerIdentities (danh tính đã gắn của Owner, EKYC-27).</summary>
public interface IOwnerIdentityRepository
{
    /// <summary>Danh tính đã gắn của Owner. Chưa có → null.</summary>
    Task<OwnerIdentity?> FindByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" một danh tính.</summary>
    void Add(OwnerIdentity identity);
}
