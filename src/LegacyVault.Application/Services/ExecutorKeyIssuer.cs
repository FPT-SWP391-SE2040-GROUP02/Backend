using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Tạo hoặc đổi mã mở gói bàn giao do server sinh. Lớp này chỉ đánh dấu thay đổi (shard và audit),
/// KHÔNG gọi SaveChanges: nơi gọi lưu chung trong giao dịch của mình.
/// </summary>
/// <remarks>
/// SEC-07: nhật ký chỉ ghi phiên bản, không ghi mã gốc hay hash. Mã gốc không được trả ra ngoài ở đây.
/// </remarks>
public sealed class ExecutorKeyIssuer
{
    private readonly IExecutorKeyShardRepository shardRepository;
    private readonly IExecutorKeyProtector keyProtector;
    private readonly IAuditLogRepository auditLogRepository;

    public ExecutorKeyIssuer(
        IExecutorKeyShardRepository shardRepository,
        IExecutorKeyProtector keyProtector,
        IAuditLogRepository auditLogRepository)
    {
        this.shardRepository = shardRepository;
        this.keyProtector = keyProtector;
        this.auditLogRepository = auditLogRepository;
    }

    /// <summary>
    /// Đảm bảo kho có mã đọc lại được trên hệ thống. Chưa có mã thì tạo phiên bản 1; có mã cũ chỉ có hash
    /// (không có bản mã hóa) thì tạo mã mới, phiên bản tăng 1. Đã có bản mã hóa thì không làm gì.
    /// Trả true khi có thay đổi cần lưu.
    /// </summary>
    public async Task<bool> EnsureReadableAsync(
        Vault vault, AuditActorRole actorRole, Guid? actorUserId, DateTimeOffset now, CancellationToken ct)
    {
        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            string firstText = ExecutorKeyRules.GenerateShard();
            ExecutorKeyShard created = new ExecutorKeyShard();
            created.VaultId = vault.Id;
            created.Version = 1;
            created.ShardHash = ExecutorKeyRules.Hash(vault.Id, created.Version, firstText);
            created.IssuedAt = now;
            keyProtector.Protect(created, firstText);
            shardRepository.Add(created);
            AddAudit(vault, AuditAction.ExecutorKeyIssued, actorRole, actorUserId, created.Version, now);
            return true;
        }

        if (!string.IsNullOrEmpty(shard.EncryptedShard))
        {
            return false;
        }

        // Mã cấp trước khi có bản mã hóa: không đọc lại được nên phải tạo mã mới.
        Rotate(vault, shard, actorRole, actorUserId, now);
        return true;
    }

    /// <summary>
    /// Đổi mã khi kho đã có mã (ví dụ đổi Người thực thi để người cũ không còn mã đúng). Chưa có mã thì không làm gì.
    /// Trả true khi có thay đổi cần lưu.
    /// </summary>
    public async Task<bool> RotateIfExistsAsync(
        Vault vault, AuditActorRole actorRole, Guid? actorUserId, DateTimeOffset now, CancellationToken ct)
    {
        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            return false;
        }
        Rotate(vault, shard, actorRole, actorUserId, now);
        return true;
    }

    private void Rotate(Vault vault, ExecutorKeyShard shard, AuditActorRole actorRole, Guid? actorUserId, DateTimeOffset now)
    {
        string text = ExecutorKeyRules.GenerateShard();
        shard.Version = shard.Version + 1;
        shard.ShardHash = ExecutorKeyRules.Hash(vault.Id, shard.Version, text);
        shard.ReissuedAt = now;
        keyProtector.Protect(shard, text);
        AddAudit(vault, AuditAction.ExecutorKeyReissued, actorRole, actorUserId, shard.Version, now);
    }

    private void AddAudit(
        Vault vault, AuditAction action, AuditActorRole actorRole, Guid? actorUserId, int version, DateTimeOffset now)
    {
        auditLogRepository.Add(AuditRules.Create(
            vault.Id, actorUserId, actorRole, action, AuditTargetType.Vault, vault.Id, version,
            AuditOutcome.Succeeded, null, now));
    }
}
