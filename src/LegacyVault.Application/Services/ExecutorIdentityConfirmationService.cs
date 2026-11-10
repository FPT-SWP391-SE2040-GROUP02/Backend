using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - BR-122: phương án dự phòng khi eKYC không chạy được. Executor tự gặp hoặc liên lạc với người nhận ngoài hệ thống, rồi bấm xác nhận.
/// - Executor không tự xác nhận cho lượt do chính tài khoản của mình nhận, nếu không Executor kiêm người nhận sẽ tự mở gói mà không qua eKYC.
/// - Lượt chỉ sang READY_TO_ACCEPT khi đang IDENTITY_IN_PROGRESS, cùng luật chặn (kế hoạch, hold) với bước hoàn tất xác minh của người nhận.
/// - Lưu Executor nào xác nhận và lúc nào để truy lại khi có tranh chấp.
/// </remarks>
public sealed class ExecutorIdentityConfirmationService : IExecutorIdentityConfirmationService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IHandoverRepository handover;
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ExecutorIdentityConfirmationService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IHandoverProgressReader progressReader,
        IHandoverRepository handover,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.progressReader = progressReader;
        this.handover = handover;
        this.holdReader = holdReader;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ConfirmHandoverIdentityResponse> ConfirmAsync(Guid caseId, Guid turnId, CancellationToken ct)
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

        // Bấm lặp: chính Executor này đã xác nhận rồi thì trả lại kết quả cũ, không đặt lại hạn quyết định.
        if (turn.IdentityConfirmedByUserId == userId && turn.IdentityConfirmedAt != null && turn.DecisionDeadline != null)
        {
            return BuildResponse(turn);
        }

        if (turn.ClaimantUserId == userId)
        {
            throw NotAllowed("OWN_TURN", "Bạn không tự xác nhận được lượt do chính tài khoản của bạn nhận.");
        }
        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa xác nhận được.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverRules.MarkReadyToAccept(turn, now);
        turn.IdentityConfirmedByUserId = userId;
        turn.IdentityConfirmedAt = now;
        // Chạm kế hoạch để RowVersion của nó chặn việc này chạy cùng lúc với đặt hold hay báo còn sống (ALIVE-01).
        vault.Plan.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        return BuildResponse(turn);
    }

    private static ConfirmHandoverIdentityResponse BuildResponse(HandoverTurn turn)
    {
        ConfirmHandoverIdentityResponse response = new ConfirmHandoverIdentityResponse();
        response.TurnId = turn.Id;
        response.Status = turn.Status.ToApiString();
        if (turn.IdentityConfirmedAt != null)
        {
            response.IdentityConfirmedAt = turn.IdentityConfirmedAt.Value;
        }
        if (turn.DecisionDeadline != null)
        {
            response.DecisionDeadline = turn.DecisionDeadline.Value;
        }
        return response;
    }

    /// <summary>Executor đang hiệu lực của kho và không phải Owner, cùng luật với phần xem tiến độ.</summary>
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

    private static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
