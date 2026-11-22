using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.TestSupport;

// Các class giả dùng chung để test service mà không cần SQL Server. Dữ liệu chỉ nằm trong List.

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated
    {
        get { return UserId != null; }
    }
}

public sealed class FakeVaultRepository : IVaultRepository
{
    public List<Vault> Vaults { get; } = new List<Vault>();

    public Task<Vault?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return Task.FromResult(Vaults.FirstOrDefault(v => v.Id == id));
    }

    public Task<Vault?> FindByOwnerIdAsync(Guid ownerId, CancellationToken ct)
    {
        return Task.FromResult(Vaults.FirstOrDefault(v => v.OwnerId == ownerId));
    }

    public Task<bool> TryAddAsync(Vault vault, CancellationToken ct)
    {
        Vaults.Add(vault);
        return Task.FromResult(true);
    }

    public Task<long> SumUsedBytesAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(0L);
    }
}

/// <summary>Executor nhận được việc giả: trả đúng danh sách đã nạp cho mọi kho.</summary>
public sealed class FakeAvailableExecutorReader : LegacyVault.Application.Repositories.IExecutorReader
{
    public List<Executor> Available { get; } = new List<Executor>();

    public Task<List<Executor>> GetActiveAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(new List<Executor>(Available));
    }

    public Task<List<Executor>> GetAvailableAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(new List<Executor>(Available));
    }
}

/// <summary>
/// Các test đổi cấu hình dùng chung của cả tiến trình (RuntimeSettings) nên chạy riêng, không song song với test khác.
/// </summary>
[Xunit.CollectionDefinition(SettingsCollection.Name, DisableParallelization = true)]
public sealed class SettingsCollection
{
    public const string Name = "RuntimeSettingsSerial";
}
