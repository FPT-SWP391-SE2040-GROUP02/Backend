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

    // Ba phụ thuộc dưới đây luôn có khi chạy thật (đăng ký đủ ở HandoverApplicationConfig).
    // Chỉ một số test bỏ trống (null) để khỏi phải dựng thêm; bỏ trống thì bước tương ứng được bỏ qua.
    private readonly IHandoverContactReader? contactReader;
    private readonly ExecutorKeyIssuer? keyIssuer;
    private readonly IWillVersionRepository? willVersions;

    public HandoverStarter(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IBeneficiaryDataProtector protector,
        ITokenService tokens,
        IHandoverInvitationSender sender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IHandoverContactReader? contactReader = null,
        ExecutorKeyIssuer? keyIssuer = null,
        IWillVersionRepository? willVersions = null)
    {
        this.willVersions = willVersions;
        this.contactReader = contactReader;
        this.keyIssuer = keyIssuer;
        this.vaults = vaults;
        this.handover = handover;
        this.protector = protector;
        this.tokens = tokens;
        this.sender = sender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    // Mở bàn giao cho một kho. Được gọi ngay khi Người xét duyệt duyệt hồ sơ chứng tử, trong cùng một lần lưu.
    public async Task<HandoverStartResult> StartAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Tìm kho
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy kho.");
        }

        HandoverPlan plan = vault.Plan;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // 2. Đã mở bàn giao rồi thì trả kết quả cũ; kế hoạch phải đang ở trạng thái mở hồ sơ chứng tử
        // Gọi lặp (ví dụ Verifier bấm duyệt lại) không tạo lượt mới và không gửi lại lời mời.
        // Lượt đã hủy (Cancelled) chỉ còn là lịch sử, không tính là đã bắt đầu: nếu tính thì hồ sơ chứng tử
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
            HandoverStartResult already = new HandoverStartResult
            {
                PlanId = plan.Id,
                TurnCount = startedTurnCount,
                AlreadyStarted = true
            };
            return already;
        }

        if (plan.Status != PlanStatus.DeathCaseOpen)
        {
            throw HandoverAccess.NotAllowed("PLAN_NOT_READY", "Kế hoạch chưa ở trạng thái mở bàn giao.");
        }

        // 3. Lấy các chỉ định gói cho người nhận, chỉ giữ phần có trong bản di chúc đã ký gần nhất
        List<Designation> designations = await handover.FindStartableDesignationsAsync(vaultId, ct);

        // Bàn giao chỉ theo bản di chúc ký gần nhất: phần thêm/đổi sau khi ký (chưa ký lại) bị bỏ qua, phần đã xóa vốn không
        // còn trong cấu hình hiện tại. Kế hoạch chưa có phiên bản ký nào (dữ liệu cũ) giữ cách cũ: theo cấu hình hiện tại.
        WillSnapshot? signedWill = null;
        if (willVersions != null)
        {
            WillVersion? latestWill = await willVersions.FindLatestByVaultIdAsync(vaultId, ct);
            if (latestWill != null)
            {
                signedWill = WillVersionRules.FromJson(latestWill.SnapshotJson);
                designations = WillVersionRules.FilterDesignations(signedWill, designations);
            }
        }
        if (designations.Count == 0)
        {
            throw HandoverAccess.NotAllowed("NOTHING_TO_HAND_OVER", "Không có gói nào sẵn sàng để bàn giao.");
        }

        // 4. Với mỗi chỉ định: tạo một lượt (token mời, hạn phản hồi, danh sách tài sản ghim lại) và soạn thư mời
        List<HandoverInvitation> invitations = new List<HandoverInvitation>();
        Dictionary<Guid, HandoverTurn> turnsById = new Dictionary<Guid, HandoverTurn>();
        HandoverContactInfo? contacts = null;
        if (contactReader != null)
        {
            contacts = await contactReader.FindAsync(vaultId, ct);
        }

        foreach (Designation designation in designations)
        {
            HashSet<Guid>? allowedAssetIds = null;
            if (signedWill != null)
            {
                allowedAssetIds = WillVersionRules.SignedAssetIds(signedWill, designation.ContainerId);
            }

            OpaqueToken token = tokens.CreateOpaqueToken();

            HandoverTurn turn = new HandoverTurn
            {
                VaultId = vaultId,
                PlanId = plan.Id,
                ContainerId = designation.ContainerId,
                ContainerName = designation.Container.Name,
                DesignationId = designation.Id,
                BeneficiaryId = designation.BeneficiaryId,
                Status = HandoverTurnStatus.Invited,
                InvitationTokenHash = token.Hash,
                InvitedAt = now,
                ResponseDeadline = now + HandoverRules.ResponseWindow,
                CreatedAt = now
            };
            turn.Items = BuildSnapshot(turn, designation.Container, allowedAssetIds);
            if (signedWill != null && turn.Items.Count == 0)
            {
                // Tất cả tài sản của gói trong bản ký đã bị gỡ (tài sản hiện có đều là thêm sau khi ký): không có gì để bàn giao.
                continue;
            }

            handover.AddTurn(turn);
            turnsById[turn.Id] = turn;

            BeneficiaryPersonalData data = protector.Unprotect(designation.Beneficiary);
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
                invitations.Add(invitation);
            }
        }

        if (turnsById.Count == 0)
        {
            throw HandoverAccess.NotAllowed("NOTHING_TO_HAND_OVER", "Không có gói nào sẵn sàng để bàn giao.");
        }

        // 5. Đảm bảo kho có khóa mở gói cho Người thực thi, chuyển kế hoạch sang đang bàn giao, lưu một lần
        // Bắt đầu bàn giao thì Người thực thi đọc được mã: đảm bảo kho có mã đọc được (chưa có hoặc chỉ có hash thì tạo mới).
        // Lưu chung giao dịch với việc đổi trạng thái kế hoạch.
        if (keyIssuer != null)
        {
            await keyIssuer.EnsureReadableAsync(vault, AuditActorRole.System, null, now, ct);
        }

        plan.Status = PlanStatus.HandoverInProgress;
        plan.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);

        HandoverStartResult result = new HandoverStartResult
        {
            PlanId = plan.Id,
            TurnCount = turnsById.Count
        };

        // 6. Gửi thư mời; thư lỗi được ghi số lần thử để job gửi lại, rồi lưu kết quả gửi
        // Gửi sau khi đã lưu: lỗi gửi không làm mất lượt, chỉ để InvitationSentAt = null.
        foreach (HandoverInvitation invitation in invitations)
        {
            try
            {
                await sender.SendAsync(invitation, ct);
                HandoverTurn sentTurn = turnsById[invitation.TurnId];
                sentTurn.InvitationSentAt = timeProvider.GetUtcNow();
                result.InvitationsSent++;
            }
            catch (Exception)
            {
                result.InvitationsFailed++;
                // Ghi nhận lần thử lỗi để job gửi lại (HandoverInvitationRetryJob) biết đã thử và giãn cách đúng.
                HandoverTurn failedTurn = turnsById[invitation.TurnId];
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

    /// <summary>
    /// Ghim từng tài sản trong gói (snapshot: sửa tài sản sau này không đổi lượt). allowedAssetIds khác null thì chỉ ghim
    /// tài sản có trong bản di chúc đã ký; null là cách cũ (ghim mọi tài sản hiện có).
    /// </summary>
    private static List<HandoverSnapshotItem> BuildSnapshot(
        HandoverTurn turn, Container container, HashSet<Guid>? allowedAssetIds)
    {
        List<HandoverSnapshotItem> items = new List<HandoverSnapshotItem>();
        int position = 0;

        foreach (ContainerAsset link in container.Assets.OrderBy(a => a.AddedAt).ThenBy(a => a.Id))
        {
            if (allowedAssetIds != null && !allowedAssetIds.Contains(link.AssetId))
            {
                continue;
            }
            Asset asset = link.Asset;
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
            items.Add(item);
            position++;
        }

        return items;
    }
}
