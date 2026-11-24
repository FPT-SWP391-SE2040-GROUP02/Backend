using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Khi luật sư chốt biên bản: mỗi người nhận trong biên bản có đúng một lượt bàn giao còn hiệu lực, ghim đúng tài sản
/// của biên bản. Người nhận mới thì tạo lượt và soạn thư mời; người đã có lượt thì giữ lượt (giữ cả danh tính đã xác nhận),
/// chỉ ghim lại tài sản; người bị bỏ khỏi biên bản thì lượt bị hủy.
/// </summary>
public sealed class HandoverRecordTurnSync
{
    /// <summary>Tên hiển thị của "gói" trên lượt: lượt giờ đi theo biên bản, không theo gói của Chủ kho.</summary>
    public const string TurnTitle = "Biên bản bàn giao";

    private readonly IHandoverRepository handover;
    private readonly IHandoverRecordRepository records;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IAssetAssignmentRepository assignments;
    private readonly IBeneficiaryDataProtector protector;
    private readonly ITokenService tokens;
    private readonly IHandoverInvitationSender sender;
    private readonly IHandoverContactReader contactReader;
    private readonly TimeProvider timeProvider;

    public HandoverRecordTurnSync(
        IHandoverRepository handover,
        IHandoverRecordRepository records,
        IBeneficiaryRepository beneficiaries,
        IAssetAssignmentRepository assignments,
        IBeneficiaryDataProtector protector,
        ITokenService tokens,
        IHandoverInvitationSender sender,
        IHandoverContactReader contactReader,
        TimeProvider timeProvider)
    {
        this.handover = handover;
        this.records = records;
        this.beneficiaries = beneficiaries;
        this.assignments = assignments;
        this.protector = protector;
        this.tokens = tokens;
        this.sender = sender;
        this.contactReader = contactReader;
        this.timeProvider = timeProvider;
    }

    /// <summary>Đối chiếu lượt với dòng biên bản. Chưa lưu: người gọi lưu một lần rồi gọi SendInvitationsAsync.</summary>
    public async Task<List<HandoverInvitation>> SyncAsync(HandoverRecord record, DateTimeOffset now, CancellationToken ct)
    {
        // 1. Gom tài sản theo người nhận, giữ thứ tự dòng
        Dictionary<Guid, List<Guid>> assetsByRecipient = HandoverRecordRules.GroupAssetsByRecipient(record.Lines);

        // 2. Lượt hiện có của biên bản, theo người nhận (bỏ lượt đã hủy)
        Dictionary<Guid, HandoverTurn> turnByRecipient = await FindActiveTurnsAsync(record, ct);

        // 3. Người bị bỏ khỏi biên bản: hủy lượt
        foreach (KeyValuePair<Guid, HandoverTurn> pair in turnByRecipient)
        {
            if (!assetsByRecipient.ContainsKey(pair.Key))
            {
                HandoverTurn removed = pair.Value;
                removed.Status = HandoverTurnStatus.Cancelled;
                removed.EndedAt = now;
                removed.UpdatedAt = now;
            }
        }

        // 4. Người còn trong biên bản: ghim lại tài sản; người mới: tạo lượt và soạn thư mời
        HandoverContactInfo contacts = await contactReader.FindAsync(record.VaultId, ct);
        HashSet<Guid> ownerRecipientIds = await FindOwnerRecipientIdsAsync(record.VaultId, ct);
        List<HandoverInvitation> invitations = new List<HandoverInvitation>();
        foreach (KeyValuePair<Guid, List<Guid>> pair in assetsByRecipient)
        {
            List<Asset> assets = new List<Asset>();
            foreach (Guid assetId in pair.Value)
            {
                Asset? asset = await handover.FindAssetAsync(assetId, ct);
                if (asset != null && asset.VaultId == record.VaultId && asset.ContentDeletedAt == null)
                {
                    assets.Add(asset);
                }
            }

            if (turnByRecipient.ContainsKey(pair.Key))
            {
                HandoverTurn kept = turnByRecipient[pair.Key];
                ReplaceItems(kept, assets, true);
                kept.UpdatedAt = now;
                continue;
            }

            OpaqueToken token = tokens.CreateOpaqueToken();
            HandoverTurn turn = new HandoverTurn
            {
                VaultId = record.VaultId,
                PlanId = record.PlanId,
                RecordId = record.Id,
                ContainerName = TurnTitle,
                BeneficiaryId = pair.Key,
                Status = HandoverTurnStatus.Invited,
                InvitationTokenHash = token.Hash,
                InvitedAt = now,
                ResponseDeadline = now + HandoverRules.ResponseWindow,
                CreatedAt = now
            };
            ReplaceItems(turn, assets, false);
            handover.AddTurn(turn);

            Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(record.VaultId, pair.Key, ct);
            if (beneficiary == null)
            {
                continue;
            }
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            if (!string.IsNullOrWhiteSpace(data.Email))
            {
                HandoverInvitation invitation = new HandoverInvitation
                {
                    TurnId = turn.Id,
                    ToEmail = data.Email,
                    FullName = data.FullName,
                    Token = token.Value,
                    ResponseDeadline = turn.ResponseDeadline
                };
                invitation.ApplyContacts(contacts);
                invitation.OwnerMessage = data.Message;
                // Người không có trong cách chia ban đầu của Chủ kho là người luật sư thêm: thư mời dùng lời riêng.
                if (!ownerRecipientIds.Contains(pair.Key))
                {
                    invitation.AddedByVerifier = true;
                }
                invitations.Add(invitation);
            }
        }
        return invitations;
    }

    /// <summary>Người nhận mà Chủ kho đã chia ít nhất một tài sản (cách chia ban đầu của kho).</summary>
    private async Task<HashSet<Guid>> FindOwnerRecipientIdsAsync(Guid vaultId, CancellationToken ct)
    {
        HashSet<Guid> result = new HashSet<Guid>();
        List<AssetAssignment> ownerAssignments = await assignments.FindByVaultIdAsync(vaultId, ct);
        foreach (AssetAssignment assignment in ownerAssignments)
        {
            result.Add(assignment.BeneficiaryId);
        }
        return result;
    }

    /// <summary>Lượt còn hiệu lực (chưa hủy) của biên bản, theo người nhận.</summary>
    public async Task<Dictionary<Guid, HandoverTurn>> FindActiveTurnsAsync(HandoverRecord record, CancellationToken ct)
    {
        List<HandoverTurn> allTurns = await records.FindTurnsWithItemsAsync(record.Id, ct);
        Dictionary<Guid, HandoverTurn> turnByRecipient = new Dictionary<Guid, HandoverTurn>();
        foreach (HandoverTurn turn in allTurns)
        {
            if (turn.Status == HandoverTurnStatus.Cancelled)
            {
                continue;
            }
            turnByRecipient[turn.BeneficiaryId] = turn;
        }
        return turnByRecipient;
    }

    /// <summary>
    /// Gửi thư mời sau khi đã lưu: lỗi gửi không làm mất lượt, chỉ ghi số lần thử để job gửi lại.
    /// Người gọi lưu thêm một lần sau hàm này.
    /// </summary>
    public async Task SendInvitationsAsync(List<HandoverInvitation> invitations, CancellationToken ct)
    {
        foreach (HandoverInvitation invitation in invitations)
        {
            HandoverTurn? turn = await handover.FindTurnByIdAsync(invitation.TurnId, ct);
            if (turn == null)
            {
                continue;
            }
            try
            {
                await sender.SendAsync(invitation, ct);
                turn.InvitationSentAt = timeProvider.GetUtcNow();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                turn.InvitationAttempts = turn.InvitationAttempts + 1;
                turn.InvitationLastAttemptAt = timeProvider.GetUtcNow();
            }
        }
    }

    /// <summary>
    /// Ghim lại danh sách tài sản của lượt (bản ghim không đổi khi Chủ kho sửa tài sản sau này).
    /// existingTurn = true khi lượt đã có trong DB: tài sản ghim mới phải được thêm tường minh, vì Id đã có sẵn
    /// nên EF sẽ tưởng là dòng cũ cần sửa nếu chỉ thêm qua danh sách của lượt.
    /// </summary>
    private void ReplaceItems(HandoverTurn turn, List<Asset> assets, bool existingTurn)
    {
        List<HandoverSnapshotItem> old = new List<HandoverSnapshotItem>(turn.Items);
        foreach (HandoverSnapshotItem item in old)
        {
            turn.Items.Remove(item);
            records.RemoveSnapshotItem(item);
        }

        int position = 0;
        foreach (Asset asset in assets)
        {
            HandoverSnapshotItem item = new HandoverSnapshotItem
            {
                TurnId = turn.Id,
                AssetId = asset.Id,
                Name = asset.Name,
                Kind = asset.Kind,
                FileName = asset.FileName,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                Position = position
            };
            turn.Items.Add(item);
            if (existingTurn)
            {
                records.AddSnapshotItem(item);
            }
            position++;
        }
    }
}
