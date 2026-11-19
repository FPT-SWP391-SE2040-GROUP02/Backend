using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Người thực thi gửi khóa mở gói qua email cho người nhận của một lượt.</summary>
/// <remarks>
/// - Khóa mở gói không lưu ở đâu, DB chỉ có mã băm; Người thực thi nhập lại khóa mình đang giữ, Backend băm để kiểm rồi mới gửi.
/// - Email người nhận chỉ giải mã ở đây để gửi; Người thực thi không thấy email.
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

    // Nút "Gửi mã qua email" của Người thực thi cho một lượt đã duyệt biên bản.
    public async Task<SendHandoverKeyResponse> SendAsync(Guid caseId, Guid turnId, string? key, CancellationToken ct)
    {
        // 1. Tìm hồ sơ chứng tử và kho; người gọi phải là Người thực thi đang phụ trách kho
        Guid userId = currentUser.RequireUserId();
        ExecutorCaseAccess access = await HandoverAccess.FindCaseForExecutorAsync(
            progressReader, vaults, executorReader, caseId, userId, ct);
        DeathCase deathCase = access.DeathCase;
        Vault vault = access.Vault;

        // 2. Lượt phải thuộc kho này; kế hoạch đang bàn giao hoặc đã đóng; lượt còn cần khóa
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.PlanId != deathCase.PlanId || turn.VaultId != vault.Id)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound,
                "Không tìm thấy nội dung bàn giao.");
        }
        if (vault.Plan.Status != PlanStatus.HandoverInProgress && vault.Plan.Status != PlanStatus.Closed)
        {
            throw NotAllowed(AppErrorType.Conflict, "PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }
        // Chỉ gửi mã khi người nhận đã đồng ý và biên bản đã được duyệt (HandoverCommitted) mà chưa nhập mã.
        // Trước đó người nhận chưa xác minh xong nên chưa được có mã; nhập rồi thì không cần gửi nữa.
        bool committedWithoutKey = turn.Status == HandoverTurnStatus.HandoverCommitted && turn.KeyVerifiedAt == null;
        if (!committedWithoutKey)
        {
            throw NotAllowed(AppErrorType.Conflict, "WRONG_STATE",
                "Chỉ gửi được mã khi biên bản đồng ý của người nhận đã được duyệt và người nhận chưa nhập mã.");
        }

        // 3. Kho phải đã cấp khóa và khóa Người thực thi nhập phải đúng
        string enteredKey = string.Empty;
        if (key != null)
        {
            enteredKey = key;
        }
        ExecutorKeyShard? shard = await keyShards.FindByVaultIdAsync(vault.Id, ct);
        if (shard == null)
        {
            throw NotAllowed(AppErrorType.Conflict, "NO_KEY", "Kho này chưa cấp khóa mở gói.");
        }
        if (!ExecutorKeyRules.Matches(vault.Id, shard.Version, enteredKey, shard.ShardHash))
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.HandoverKeyInvalid, "Khóa mở gói chưa đúng.");
        }

        // 4. Lấy email người nhận; không có email thì không gửi được
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

        // 5. Gửi thư chứa khóa; lỗi gửi thì báo thử lại, không ghi chi tiết vì thư có khóa
        HandoverKeyMail mail = new HandoverKeyMail
        {
            TurnId = turn.Id,
            ToEmail = email,
            FullName = fullName,
            Key = enteredKey.Trim()
        };
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

        // 6. Trả thời điểm đã gửi
        SendHandoverKeyResponse response = new SendHandoverKeyResponse
        {
            TurnId = turn.Id,
            SentAt = timeProvider.GetUtcNow()
        };
        return response;
    }

    /// <summary>Lỗi không được phép; reason là mã lý do để giao diện hiện đúng câu.</summary>
    private static AppException NotAllowed(AppErrorType type, string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(type, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
