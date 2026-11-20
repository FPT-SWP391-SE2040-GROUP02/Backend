using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using Xunit;

namespace LegacyVault.IntegrationTests.TestSupport;

// Các class giả dùng chung cho test không cần SQL Server. Dữ liệu chỉ nằm trong List.

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

/// <summary>
/// Các test đổi cấu hình dùng chung của cả tiến trình (RuntimeSettings) nên chạy riêng, không song song với test khác.
/// </summary>
[CollectionDefinition(SettingsCollection.Name, DisableParallelization = true)]
public sealed class SettingsCollection
{
    public const string Name = "RuntimeSettingsSerial";
}
