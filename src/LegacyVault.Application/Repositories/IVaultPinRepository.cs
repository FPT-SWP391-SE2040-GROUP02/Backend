using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng VaultPins.</summary>
public interface IVaultPinRepository
{
    /// <summary>Mã PIN của kho (có theo dõi thay đổi). Chưa đặt → null.</summary>
    Task<VaultPin?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" PIN mới.</summary>
    void Add(VaultPin pin);

    /// <summary>true khi lỗi là vi phạm unique (hai request cùng đặt PIN lần đầu cho một kho).</summary>
    bool IsUniqueViolation(Exception exception);

    /// <summary>Bỏ các thay đổi đang chờ sau khi một lần lưu thất bại.</summary>
    void DiscardPendingChanges();
}
