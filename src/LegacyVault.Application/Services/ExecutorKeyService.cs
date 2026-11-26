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
    private readonly ExecutorKeyIssuer keyIssuer;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IExecutorKeyProtector keyProtector;
    private readonly IUserRepository userRepository;
    private readonly IExecutorReader executorReader;

    public ExecutorKeyService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorKeyShardRepository shardRepository,
        ExecutorKeyIssuer keyIssuer,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IExecutorKeyProtector keyProtector,
        IUserRepository userRepository,
        IExecutorReader executorReader)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.shardRepository = shardRepository;
        this.keyIssuer = keyIssuer;
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

    public async Task<ExecutorKeyStatusResponse> ReissueAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultRules.EnsureEditable(vault);
        DateTimeOffset now = timeProvider.GetUtcNow();
        bool rotated = await keyIssuer.RotateIfExistsAsync(vault, AuditActorRole.Owner, vault.OwnerId, now, ct);
        if (!rotated)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.ExecutorKeyNotIssued,
                "Kho chưa có mã nhận nên chưa có gì để đổi.");
        }
        await unitOfWork.SaveChangesAsync(ct);

        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        ExecutorKeyStatusResponse response = new ExecutorKeyStatusResponse();
        Fill(response, vault, shard);
        return response;
    }

    public async Task<ExecutorHandoverKeyResponse> GetKeyForExecutorAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        // Chỉ Người thực thi ĐÃ CHẤP NHẬN của kho mới đọc được mã; lời mời chưa trả lời, Chủ tài sản hay người lạ
        // đều nhận 404 như kho không tồn tại (không lộ kho nào có mã).
        if (vault == null || vault.OwnerId == userId || !await IsAcceptedExecutorAsync(vault.Id, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        // Mã chỉ xem được sau khi luật sư chốt biên bản bàn giao (kế hoạch HandoverInProgress). Closed vẫn cho xem:
        // lượt cuối được nhận thì kế hoạch tự đóng, mà người nhận còn phải nhập mã mới tải được (VerifyKeyAsync cho phép lượt đã nhận).
        if (vault.Plan.Status != PlanStatus.HandoverInProgress && vault.Plan.Status != PlanStatus.Closed)
        {
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = "HANDOVER_NOT_STARTED";
            throw new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed,
                "Mã nhận chỉ xem được sau khi luật sư chốt biên bản bàn giao.", extensions);
        }

        ExecutorKeyShard? shard = await shardRepository.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.ExecutorKeyNotIssued, "Kho chưa được cấp mã nhận.");
        }

        string shardText;
        try
        {
            shardText = keyProtector.Unprotect(shard);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException
            || exception is FormatException || exception is KeyNotFoundException || exception is InvalidOperationException)
        {
            // Không đưa chi tiết lỗi giải mã ra ngoài; Chủ tài sản cấp lại là có mã mới.
            shardText = string.Empty;
        }
        if (string.IsNullOrEmpty(shardText))
        {
            // Mã cấp trước khi có tính năng gửi trên hệ thống (chỉ có hash) hoặc không giải mã được.
            throw new AppException(AppErrorType.Conflict, ErrorCode.ExecutorKeyNotIssued,
                "Mã nhận của kho này chưa đọc được trên hệ thống. Chủ tài sản cần đổi mã.");
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
}
