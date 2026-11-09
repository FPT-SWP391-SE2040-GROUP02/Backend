using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class HandoverStarter : IHandoverStarter
{
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly ITokenService tokens;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverStarter(
        IVaultRepository vaults,
        IHandoverRepository handover,
        ITokenService tokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.vaults = vaults;
        this.handover = handover;
        this.tokens = tokens;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<HandoverStartResult> StartAsync(Guid vaultId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy kho.");
        }

        HandoverPlan plan = vault.Plan;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Gọi lặp (F2B retry) không tạo lượt mới và không gửi lại lời mời.
        // Lượt đã hủy (Owner còn sống, ALIVE-06) chỉ còn là lịch sử, không tính là đã bắt đầu: nếu tính thì hồ sơ chứng tử
        // mới của cùng kho không bao giờ mở được bàn giao. Chỉ mục UX_HandoverTurns_ActivePerDesignation không tính lượt
        // Cancelled nên lượt mới cho cùng chỉ định không bị chặn ở database.
        List<HandoverTurn> existing = await handover.FindTurnsByPlanAsync(plan.Id, ct);
        int startedTurnCount = 0;
        foreach (HandoverTurn existingTurn in existing)
        {
            if (existingTurn.Status != HandoverTurnStatus.Cancelled)
            {
                startedTurnCount++;
            }
        }
        if (startedTurnCount > 0)
        {
            HandoverStartResult already = new HandoverStartResult();
            already.PlanId = plan.Id;
            already.TurnCount = startedTurnCount;
            already.AlreadyStarted = true;
            return already;
        }

        if (plan.Status != PlanStatus.DeathCaseOpen)
        {
            throw NotAllowed("PLAN_NOT_READY", "Kế hoạch chưa ở trạng thái mở bàn giao.");
        }

        List<Designation> designations = await handover.FindStartableDesignationsAsync(vaultId, ct);
        if (designations.Count == 0)
        {
            throw NotAllowed("NOTHING_TO_HAND_OVER", "Không có gói nào sẵn sàng để bàn giao.");
        }

        List<HandoverTurn> turns = new List<HandoverTurn>();

        foreach (Designation designation in designations)
        {
            OpaqueToken token = tokens.CreateOpaqueToken();

            HandoverTurn turn = new HandoverTurn();
            turn.VaultId = vaultId;
            turn.PlanId = plan.Id;
            turn.ContainerId = designation.ContainerId;
            turn.ContainerName = designation.Container.Name;
            turn.DesignationId = designation.Id;
            turn.BeneficiaryId = designation.BeneficiaryId;
            turn.Status = HandoverTurnStatus.Invited;
            turn.InvitationTokenHash = token.Hash;
            turn.InvitedAt = now;
            turn.ResponseDeadline = now + HandoverRules.ResponseWindow;
            turn.CreatedAt = now;
            turn.Items = BuildSnapshot(turn, designation.Container);

            handover.AddTurn(turn);
            turns.Add(turn);
        }

        plan.Status = PlanStatus.HandoverInProgress;
        plan.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);

        HandoverStartResult result = new HandoverStartResult();
        result.PlanId = plan.Id;
        result.TurnCount = turns.Count;

        // Không tự gửi thư mời: Người thực thi bấm gửi từng lượt (HandoverInvitationService.ResendAsync).
        // InvitationSentAt để null cho tới lần gửi đầu tiên.
        return result;
    }

    /// <summary>Ghim từng tài sản trong gói (snapshot: sửa tài sản sau này không đổi lượt).</summary>
    private static List<HandoverSnapshotItem> BuildSnapshot(HandoverTurn turn, Container container)
    {
        List<HandoverSnapshotItem> items = new List<HandoverSnapshotItem>();
        int position = 0;

        foreach (ContainerAsset link in container.Assets.OrderBy(a => a.AddedAt).ThenBy(a => a.Id))
        {
            Asset asset = link.Asset;
            HandoverSnapshotItem item = new HandoverSnapshotItem();
            item.TurnId = turn.Id;
            item.AssetId = asset.Id;
            item.Name = asset.Name;
            item.Kind = asset.Kind;
            item.FileName = asset.FileName;
            item.ContentType = asset.ContentType;
            item.SizeBytes = asset.SizeBytes;
            item.Position = position;
            items.Add(item);
            position++;
        }

        return items;
    }

    private static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
