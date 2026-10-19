using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class HandoverStarter : IHandoverStarter
{
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IBeneficiaryDataProtector protector;
    private readonly ITokenService tokens;
    private readonly IHandoverInvitationSender sender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverStarter(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IBeneficiaryDataProtector protector,
        ITokenService tokens,
        IHandoverInvitationSender sender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.vaults = vaults;
        this.handover = handover;
        this.protector = protector;
        this.tokens = tokens;
        this.sender = sender;
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
        List<HandoverTurn> existing = await handover.FindTurnsByPlanAsync(plan.Id, ct);
        if (existing.Count > 0)
        {
            HandoverStartResult already = new HandoverStartResult();
            already.PlanId = plan.Id;
            already.TurnCount = existing.Count;
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

        List<HandoverInvitation> invitations = new List<HandoverInvitation>();
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

            BeneficiaryPersonalData data = protector.Unprotect(designation.Beneficiary);
            if (!string.IsNullOrWhiteSpace(data.Email))
            {
                HandoverInvitation invitation = new HandoverInvitation();
                invitation.TurnId = turn.Id;
                invitation.ToEmail = data.Email;
                invitation.FullName = data.FullName;
                invitation.Token = token.Value;
                invitation.ResponseDeadline = turn.ResponseDeadline;
                invitations.Add(invitation);
            }
        }

        plan.Status = PlanStatus.HandoverInProgress;
        plan.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);

        HandoverStartResult result = new HandoverStartResult();
        result.PlanId = plan.Id;
        result.TurnCount = turns.Count;

        // Gửi sau khi đã lưu: lỗi gửi không làm mất lượt (INVITE-01), chỉ để InvitationSentAt = null.
        foreach (HandoverInvitation invitation in invitations)
        {
            try
            {
                await sender.SendAsync(invitation, ct);
                HandoverTurn sentTurn = turns.First(t => t.Id == invitation.TurnId);
                sentTurn.InvitationSentAt = timeProvider.GetUtcNow();
                result.InvitationsSent++;
            }
            catch (Exception)
            {
                result.InvitationsFailed++;
                // Ghi nhận lần thử lỗi để job gửi lại (HandoverInvitationRetryJob) biết đã thử và giãn cách đúng.
                HandoverTurn failedTurn = turns.First(t => t.Id == invitation.TurnId);
                failedTurn.InvitationAttempts = failedTurn.InvitationAttempts + 1;
                failedTurn.InvitationLastAttemptAt = timeProvider.GetUtcNow();
            }
        }

        if (result.InvitationsSent > 0 || result.InvitationsFailed > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    /// <summary>Ghim phiên bản hiện hành của từng tài sản trong gói (snapshot: sửa tài sản sau này không đổi lượt).</summary>
    private static List<HandoverSnapshotItem> BuildSnapshot(HandoverTurn turn, Container container)
    {
        List<HandoverSnapshotItem> items = new List<HandoverSnapshotItem>();
        int position = 0;

        foreach (ContainerAsset link in container.Assets.OrderBy(a => a.AddedAt).ThenBy(a => a.Id))
        {
            Asset asset = link.Asset;
            AssetVersion? version = asset.Versions.FirstOrDefault(v => v.VersionNumber == asset.CurrentVersionNumber);
            if (version == null)
            {
                continue;
            }

            HandoverSnapshotItem item = new HandoverSnapshotItem();
            item.TurnId = turn.Id;
            item.AssetId = asset.Id;
            item.AssetVersionId = version.Id;
            item.Name = asset.Name;
            item.Kind = asset.Kind;
            item.FileName = version.FileName;
            item.ContentType = version.ContentType;
            item.SizeBytes = version.SizeBytes;
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
