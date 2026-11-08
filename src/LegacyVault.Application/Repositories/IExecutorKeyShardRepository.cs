using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng ExecutorKeyShards.</summary>
public interface IExecutorKeyShardRepository
{
    /// <summary>Mảnh của kho (có theo dõi thay đổi). Chưa cấp → null.</summary>
    Task<ExecutorKeyShard?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct);

    void Add(ExecutorKeyShard shard);

    /// <summary>true khi lỗi là vi phạm unique (hai request cùng cấp lần đầu cho một kho).</summary>
    bool IsUniqueViolation(Exception exception);

    void DiscardPendingChanges();
}
