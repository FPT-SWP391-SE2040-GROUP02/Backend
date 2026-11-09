using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SEC-07: mảnh gốc chỉ nằm trong response lúc cấp; nhật ký, message lỗi và DB chỉ có phiên bản và hash.
/// </remarks>
public sealed class ExecutorKeyService : IExecutorKeyService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorKeyShardRepository shardRepository;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ExecutorKeyService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorKeyShardRepository shardRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.shardRepository = shardRepository;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ExecutorKeyStatusResponse> GetStatusAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        ExecutorKeyStatusResponse response = new ExecutorKeyStatusResponse();
        Fill(response, vault, shard);
        return response;
    }

    public async Task<ExecutorKeyIssuedResponse> IssueAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultRules.EnsureEditable(vault);
        if (await shardRepository.FindByVaultIdAsync(vault.Id, ct) != null)
        {
            throw AlreadyIssued();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string text = ExecutorKeyRules.GenerateShard();
        ExecutorKeyShard shard = new ExecutorKeyShard();
        shard.VaultId = vault.Id;
        shard.Version = 1;
        shard.ShardHash = ExecutorKeyRules.Hash(vault.Id, shard.Version, text);
        shard.IssuedAt = now;
        shardRepository.Add(shard);
        AddAudit(vault, AuditAction.ExecutorKeyIssued, shard.Version, now);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (shardRepository.IsUniqueViolation(exception))
        {
            shardRepository.DiscardPendingChanges();
            throw AlreadyIssued();
        }
        return BuildIssued(vault, shard, text);
    }

    public async Task<ExecutorKeyIssuedResponse> ReissueAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultRules.EnsureEditable(vault);
        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.ExecutorKeyNotIssued,
                "Kho chưa cấp mảnh khóa nên chưa có gì để cấp lại.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string text = ExecutorKeyRules.GenerateShard();
        shard.Version = shard.Version + 1;
        shard.ShardHash = ExecutorKeyRules.Hash(vault.Id, shard.Version, text);
        shard.ReissuedAt = now;
        AddAudit(vault, AuditAction.ExecutorKeyReissued, shard.Version, now);
        await unitOfWork.SaveChangesAsync(ct);
        return BuildIssued(vault, shard, text);
    }

    private static AppException AlreadyIssued()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.ExecutorKeyAlreadyIssued,
            "Kho đã có mảnh khóa. Nếu Người thực thi quên, hãy dùng cấp lại.");
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    private static void Fill(ExecutorKeyStatusResponse response, Vault vault, ExecutorKeyShard? shard)
    {
        response.CanIssue = VaultRules.GetNotEditableReason(vault) == null;
        if (shard == null)
        {
            response.HasKey = false;
            response.Version = 0;
            return;
        }
        response.HasKey = true;
        response.Version = shard.Version;
        response.IssuedAt = shard.IssuedAt;
        response.ReissuedAt = shard.ReissuedAt;
    }

    private static ExecutorKeyIssuedResponse BuildIssued(Vault vault, ExecutorKeyShard shard, string text)
    {
        ExecutorKeyIssuedResponse response = new ExecutorKeyIssuedResponse();
        Fill(response, vault, shard);
        response.Shard = text;
        return response;
    }

    private void AddAudit(Vault vault, AuditAction action, int version, DateTimeOffset now)
    {
        // Chỉ ghi phiên bản, không ghi mảnh hay hash (SEC-07).
        auditLogRepository.Add(AuditRules.Create(
            vault.Id, vault.OwnerId, AuditActorRole.Owner, action, AuditTargetType.Vault, vault.Id, version,
            AuditOutcome.Succeeded, null, now));
    }
}
