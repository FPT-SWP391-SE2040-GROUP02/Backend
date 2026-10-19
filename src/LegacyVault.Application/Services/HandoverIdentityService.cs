using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class HandoverIdentityService : IHandoverIdentityService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IEkycVerificationRepository verifications;
    private readonly IHandoverService handoverService;
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverIdentityService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IHandoverRepository handover,
        IEkycVerificationRepository verifications,
        IHandoverService handoverService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
        : this(currentUser, vaults, handover, verifications, handoverService, new NoHoldReader(), unitOfWork, timeProvider)
    {
    }

    public HandoverIdentityService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IHandoverRepository handover,
        IEkycVerificationRepository verifications,
        IHandoverService handoverService,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.holdReader = holdReader;
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.handover = handover;
        this.verifications = verifications;
        this.handoverService = handoverService;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<HandoverTurnResponse> CompleteIdentityAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();

        // Id lạ và lượt của người khác cho cùng một lỗi (SEC-04).
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.ClaimantUserId != userId)
        {
            throw HandoverNotFound();
        }

        // Bấm lặp: đã sẵn sàng nhận rồi thì trả lại đúng lượt, không đặt lại hạn quyết định.
        if (turn.Status == HandoverTurnStatus.ReadyToAccept)
        {
            return await handoverService.GetTurnAsync(turnId, ct);
        }
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước xác minh danh tính.");
        }

        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw HandoverNotFound();
        }
        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }

        // ACCEPT-01, STATE-02: kho đang bị giữ thì chưa chuyển sang "sẵn sàng nhận" (cùng luật với nhận lượt và đồng ý).
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa hoàn tất xác minh được.");
        }

        EkycVerification? verification = await verifications.FindLatestByHandoverTurnAsync(userId, turnId, ct);
        string? reason = HandoverIdentityRules.GetBlockingReason(verification, turnId);
        if (reason != null)
        {
            throw NotAllowed(reason, HandoverIdentityRules.GetMessage(reason));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverRules.MarkReadyToAccept(turn, now);
        // Chạm kế hoạch để RowVersion của nó chặn việc này chạy cùng lúc với đặt hold hay báo còn sống (ALIVE-01).
        vault.Plan.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        return await handoverService.GetTurnAsync(turnId, ct);
    }

    private static AppException HandoverNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound,
            "Không tìm thấy nội dung bàn giao.");
    }

    private static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
