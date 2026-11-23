using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Phiếu giao nhận của từng người nhận, ký SAU khi giao: người nhận xác nhận đã nhận đủ (HandoverService), rồi
/// Người thực thi ký bên giao. Mọi phiếu đủ hai chữ ký thì kế hoạch đóng.
/// </summary>
public sealed class HandoverReceiptService : IHandoverReceiptService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IHandoverRepository handover;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverReceiptService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IHandoverProgressReader progressReader,
        IHandoverRepository handover,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.progressReader = progressReader;
        this.handover = handover;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task SignAsExecutorAsync(Guid recordId, Guid turnId, CancellationToken ct)
    {
        // 1. Người gọi phải là Người thực thi đang phụ trách kho; lượt phải thuộc biên bản
        Guid userId = currentUser.RequireUserId();
        ExecutorCaseAccess access = await HandoverAccess.FindCaseForExecutorAsync(
            progressReader, vaults, executorReader, recordId, userId, ct);
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.RecordId != access.Record.Id)
        {
            throw HandoverAccess.NotFound();
        }

        // 2. Người nhận phải ký trước; Người thực thi không ký cả hai bên
        if (turn.Status != HandoverTurnStatus.HandoverCommitted || turn.ReceivedAllAt == null)
        {
            throw HandoverAccess.NotAllowed("NOT_RECEIVED", "Người nhận chưa xác nhận đã nhận đủ tài sản.");
        }
        if (turn.ClaimantUserId == userId)
        {
            throw HandoverAccess.NotAllowed("SAME_PERSON", "Bạn là người nhận của phiếu này nên không ký bên giao được.");
        }
        if (turn.ReceiptExecutorSignedAt != null)
        {
            // Bấm lặp: giữ chữ ký cũ.
            return;
        }

        // 3. Ký, mọi phiếu đã xong thì đóng kế hoạch, lưu một lần
        DateTimeOffset now = timeProvider.GetUtcNow();
        turn.ReceiptExecutorSignedAt = now;
        turn.ReceiptExecutorUserId = userId;
        turn.UpdatedAt = now;
        // Chạm kế hoạch để hai phiếu cuối ký cùng lúc không bỏ lỡ việc đóng.
        access.Vault.Plan.UpdatedAt = now;
        List<HandoverTurn> turns = await handover.FindTurnsByPlanAsync(access.Vault.Plan.Id, ct);
        HandoverRules.CloseIfAllTerminal(access.Vault.Plan, turns, now);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
