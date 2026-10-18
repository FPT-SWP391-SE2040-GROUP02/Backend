using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>Đọc thẳng bảng AuditLogs để kiểm các dòng service đã ghi (chưa có API đọc nhật ký).</summary>
public static class AuditTestData
{
    /// <summary>Mọi dòng nhật ký của kho, cũ trước mới sau.</summary>
    public static async Task<List<AuditLog>> ListForVaultAsync(ApiFactory factory, Guid vaultId)
    {
        List<AuditLog> logs = new List<AuditLog>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            logs = await db.AuditLogs.AsNoTracking()
                .Where(l => l.VaultId == vaultId)
                .OrderBy(l => l.At)
                .ThenBy(l => l.Id)
                .ToListAsync();
        });
        return logs;
    }

    /// <summary>Các dòng nhật ký của kho có đúng loại thao tác.</summary>
    public static async Task<List<AuditLog>> ListForVaultAsync(ApiFactory factory, Guid vaultId, AuditAction action)
    {
        List<AuditLog> all = await ListForVaultAsync(factory, vaultId);
        List<AuditLog> matched = new List<AuditLog>();
        foreach (AuditLog log in all)
        {
            if (log.Action == action)
            {
                matched.Add(log);
            }
        }
        return matched;
    }

    /// <summary>Id chủ kho, dùng để so với ActorUserId.</summary>
    public static async Task<Guid> GetOwnerIdAsync(ApiFactory factory, Guid vaultId)
    {
        Guid ownerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.AsNoTracking().SingleAsync(v => v.Id == vaultId);
            ownerId = vault.OwnerId;
        });
        return ownerId;
    }
}
