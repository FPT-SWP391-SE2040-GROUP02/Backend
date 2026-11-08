using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - Khóa mở gói không lưu ở đâu, DB chỉ có mã băm; Người thực hiện nhập lại khóa mình đang giữ, Backend băm để kiểm rồi mới gửi.
/// - Email người nhận chỉ giải mã ở đây để gửi; Người thực hiện không thấy email (SEC-05).
/// - Khóa đi bằng thư riêng, tách khỏi thư lời mời, nên người chỉ có một trong hai thư thì chưa mở được gói.
/// </remarks>
public sealed class HandoverKeyMailService : IHandoverKeyMailService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IHandoverRepository handover;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IExecutorKeyShardRepository keyShards;
    private readonly IHandoverKeySender sender;
    private readonly TimeProvider timeProvider;

    public HandoverKeyMailService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IHandoverProgressReader progressReader,
        IHandoverRepository handover,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IExecutorKeyShardRepository keyShards,
        IHandoverKeySender sender,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.progressReader = progressReader;
        this.handover = handover;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.keyShards = keyShards;
        this.sender = sender;
        this.timeProvider = timeProvider;
    }

    public async Task<SendHandoverKeyResponse> SendAsync(Guid caseId, Guid turnId, string? key, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DeathCase? deathCase = await progressReader.FindCaseAsync(caseId, ct);
        if (deathCase == null)
        {
            throw CaseNotFound();
        }
        Vault? vault = await vaults.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw CaseNotFound();
        }

        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.PlanId != deathCase.PlanId || turn.VaultId != vault.Id)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound,
                "Không tìm thấy nội dung bàn giao.");
        }
        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed(AppErrorType.Conflict, "PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }
        if (HandoverRules.IsTerminal(turn.Status) || turn.KeyVerifiedAt != null)
        {
            throw NotAllowed(AppErrorType.Conflict, "WRONG_STATE", "Lượt này đã kết thúc hoặc người nhận đã nhập khóa.");
        }

        ExecutorKeyShard? shard = await keyShards.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            throw NotAllowed(AppErrorType.Conflict, "NO_KEY", "Kho này chưa cấp khóa mở gói.");
        }
        if (!ExecutorKeyRules.Matches(vault.Id, shard.Version, key ?? string.Empty, shard.ShardHash))
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.HandoverKeyInvalid, "Khóa mở gói chưa đúng.");
        }

        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, turn.BeneficiaryId, ct);
        string? email = null;
        string fullName = string.Empty;
        if (beneficiary != null)
        {
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            email = data.Email;
            fullName = data.FullName;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw NotAllowed(AppErrorType.Conflict, "NO_EMAIL", "Người nhận chưa có email để gửi khóa.");
        }

        HandoverKeyMail mail = new HandoverKeyMail();
        mail.TurnId = turn.Id;
        mail.ToEmail = email;
        mail.FullName = fullName;
        mail.Key = (key ?? string.Empty).Trim();
        try
        {
            await sender.SendAsync(mail, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Không ghi chi tiết lỗi: nội dung thư có khóa và email.
            throw NotAllowed(AppErrorType.ServiceUnavailable, "KEY_SEND_FAILED",
                "Chưa gửi được khóa lúc này. Vui lòng thử lại sau.");
        }

        SendHandoverKeyResponse response = new SendHandoverKeyResponse();
        response.TurnId = turn.Id;
        response.SentAt = timeProvider.GetUtcNow();
        return response;
    }

    private async Task<bool> IsActiveExecutorAsync(Vault vault, Guid userId, CancellationToken ct)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    private static AppException CaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
    }

    private static AppException NotAllowed(AppErrorType type, string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(type, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
