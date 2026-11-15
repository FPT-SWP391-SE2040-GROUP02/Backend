using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SEC-07: nhật ký và message lỗi chỉ có phiên bản; DB giữ hash để kiểm và bản mã hóa phong bì (AES-256-GCM, DEK bọc bằng KEK)
/// để Người thực thi đã chấp nhận đọc lại trên hệ thống. Mảnh gốc không bao giờ lưu dạng rõ.
/// </remarks>
public sealed class ExecutorKeyService : IExecutorKeyService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorKeyShardRepository shardRepository;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IExecutorKeyProtector keyProtector;
    private readonly IUserRepository userRepository;
    private readonly IExecutorReader executorReader;

    public ExecutorKeyService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorKeyShardRepository shardRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IExecutorKeyProtector keyProtector,
        IUserRepository userRepository,
        IExecutorReader executorReader)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.shardRepository = shardRepository;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.keyProtector = keyProtector;
        this.userRepository = userRepository;
        this.executorReader = executorReader;
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
        keyProtector.Protect(shard, text);
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
        keyProtector.Protect(shard, text);
        AddAudit(vault, AuditAction.ExecutorKeyReissued, shard.Version, now);
        await unitOfWork.SaveChangesAsync(ct);
        return BuildIssued(vault, shard, text);
    }

    public async Task<ExecutorHandoverKeyResponse> GetKeyForExecutorAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        // Chỉ Người thực thi ĐÃ CHẤP NHẬN của kho mới đọc được mã; lời mời chưa trả lời, Chủ kho hay người lạ
        // đều nhận 404 như kho không tồn tại (không lộ kho nào có mã).
        if (vault == null || vault.OwnerId == userId || !await IsAcceptedExecutorAsync(vault.Id, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.ExecutorKeyNotIssued, "Kho chưa được cấp mã mở gói.");
        }

        string shardText;
        try
        {
            shardText = keyProtector.Unprotect(shard);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException
            || exception is FormatException || exception is KeyNotFoundException || exception is InvalidOperationException)
        {
            // Không đưa chi tiết lỗi giải mã ra ngoài; Chủ kho cấp lại là có mã mới.
            shardText = string.Empty;
        }
        if (string.IsNullOrEmpty(shardText))
        {
            // Mã cấp trước khi có tính năng gửi trên hệ thống (chỉ có hash) hoặc không giải mã được.
            throw new AppException(AppErrorType.Conflict, ErrorCode.ExecutorKeyNotIssued,
                "Mã mở gói của kho này chưa được gửi trên hệ thống. Chủ kho cần cấp lại mã.");
        }

        User? owner = await userRepository.FindByIdAsync(vault.OwnerId, ct);
        ExecutorHandoverKeyResponse response = new ExecutorHandoverKeyResponse();
        response.VaultId = vault.Id;
        response.OwnerFullName = owner == null ? string.Empty : owner.FullName;
        response.Version = shard.Version;
        response.Shard = shardText;
        response.IssuedAt = shard.IssuedAt;
        response.ReissuedAt = shard.ReissuedAt;
        response.Status = "ACTIVE_ON_SYSTEM";
        return response;
    }

    private async Task<bool> IsAcceptedExecutorAsync(Guid vaultId, Guid userId, CancellationToken ct)
    {
        List<Executor> executors = await executorReader.GetAvailableAsync(vaultId, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
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
