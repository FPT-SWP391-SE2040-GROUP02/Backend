using System.Security.Cryptography;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IExecutorWillService
{
    /// <summary>
    /// Người thực thi đồng ý (ký bằng mật khẩu) hoặc không đồng ý phiên bản Chủ tài sản ký lại. Không đồng ý thì vẫn giữ vai trò
    /// theo bản đã ký trước cho tới khi có người thay. Không phải Người thực thi của kho → 404; phiên bản cũ → 409
    /// EXECUTOR_NOT_ALLOWED reason WILL_VERSION_CHANGED; kế hoạch không chạy → reason PLAN_NOT_RUNNING.
    /// </summary>
    Task RespondAsync(Guid vaultId, RespondWillVersionRequest request, CancellationToken ct);

    /// <summary>
    /// Biên bản phân chia đầy đủ (bản ký gần nhất) cho Người thực thi đã nhận vai. Không phải Người thực thi của kho → 404;
    /// kho chưa có bản ký → 409 EXECUTOR_NOT_ALLOWED reason WILL_NOT_SIGNED. Mỗi lần xem ghi nhật ký.
    /// </summary>
    Task<ExecutorWillResponse> GetForVaultAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Cùng tờ đó cho người đang được mời đọc trước khi ký nhận vai. Lời mời phải còn hạn và gửi đúng email đã xác minh của
    /// tài khoản đang đăng nhập, nếu không → 404; Chủ tài sản chưa ký bản dành cho lời mời này → 409 reason WILL_NOT_SIGNED.
    /// </summary>
    Task<ExecutorWillResponse> GetForInvitationAsync(Guid invitationId, CancellationToken ct);
}

public sealed class ExecutorWillService : IExecutorWillService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IUserRepository users;
    private readonly IWillVersionRepository willVersions;
    private readonly ISigningPasswordChecker passwordChecker;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly IExecutorWillNoticeSender notices;
    private readonly TimeProvider time;
    private readonly IExecutorInvitationRepository invitations;
    private readonly IAssetAssignmentRepository assignments;
    private readonly IAssetRepository assets;
    private readonly IHandoverRecordRepository handoverRecords;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly IOwnerIdentityRepository ownerIdentities;
    private readonly IOwnerIdentityProtector ownerProtector;

    public ExecutorWillService(ICurrentUser currentUser, IVaultRepository vaults, IUserRepository users,
        IWillVersionRepository willVersions, ISigningPasswordChecker passwordChecker, IAuditLogRepository audit,
        IUnitOfWork unitOfWork, IExecutorWillNoticeSender notices, TimeProvider time,
        IExecutorInvitationRepository invitations, IAssetAssignmentRepository assignments, IAssetRepository assets,
        IHandoverRecordRepository handoverRecords, IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector beneficiaryProtector, IOwnerIdentityRepository ownerIdentities,
        IOwnerIdentityProtector ownerProtector)
    {
        this.invitations = invitations;
        this.assignments = assignments;
        this.assets = assets;
        this.handoverRecords = handoverRecords;
        this.beneficiaries = beneficiaries;
        this.beneficiaryProtector = beneficiaryProtector;
        this.ownerIdentities = ownerIdentities;
        this.ownerProtector = ownerProtector;
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.users = users;
        this.willVersions = willVersions;
        this.passwordChecker = passwordChecker;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.notices = notices;
        this.time = time;
    }

    public async Task RespondAsync(Guid vaultId, RespondWillVersionRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản hiện không được phép thực hiện thao tác.");
        }

        // 1. Kiểm quyền và phiên bản trước, rồi mới kiểm mật khẩu ngoài giao dịch để bộ đếm sai được giữ lại
        await LoadAsync(vaultId, userId, request, ct);
        if (request.Accept)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw ValidationErrors.For("password", "Nhập mật khẩu tài khoản để ký biên bản phân chia.");
            }
            await passwordChecker.CheckAsync(user, request.Password, time.GetUtcNow(), ct);
        }

        // 2. Ghi câu trả lời; bấm lặp cùng câu trả lời thì giữ nguyên
        Guid? notifyOwnerId = null;
        await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            notifyOwnerId = null;
            (Vault vault, Executor executor) = await LoadAsync(vaultId, userId, request, ct);
            DateTimeOffset now = time.GetUtcNow();
            if (request.Accept && executor.ConfirmedWillVersionNo != request.WillVersionNo)
            {
                executor.ConfirmedWillVersionNo = request.WillVersionNo;
                executor.WillConfirmedAt = now;
                executor.DeclinedWillVersionNo = null;
                executor.UpdatedAt = now;
                audit.Add(AuditRules.Create(vault.Id, userId, AuditActorRole.Executor, AuditAction.ExecutorWillConfirmed,
                    AuditTargetType.Vault, vault.Id, request.WillVersionNo, AuditOutcome.Succeeded, null, now));
            }
            if (!request.Accept && executor.DeclinedWillVersionNo != request.WillVersionNo)
            {
                if (executor.ConfirmedWillVersionNo == request.WillVersionNo)
                {
                    throw NotAllowed("WILL_ALREADY_CONFIRMED");
                }
                executor.DeclinedWillVersionNo = request.WillVersionNo;
                executor.UpdatedAt = now;
                audit.Add(AuditRules.Create(vault.Id, userId, AuditActorRole.Executor, AuditAction.ExecutorWillDeclined,
                    AuditTargetType.Vault, vault.Id, request.WillVersionNo, AuditOutcome.Succeeded, null, now));
                notifyOwnerId = vault.OwnerId;
            }
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);

        // 3. Không đồng ý thì báo Chủ tài sản sau khi đã lưu
        if (notifyOwnerId != null)
        {
            User? owner = await users.FindByIdAsync(notifyOwnerId.Value, ct);
            if (owner != null)
            {
                await notices.SendDeclinedAsync(owner.Email, user.FullName, request.WillVersionNo, ct);
            }
        }
    }

    public async Task<ExecutorWillResponse> GetForVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        await RequireActiveUserAsync(userId, ct);
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null || !IsAcceptedExecutor(vault, userId))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        if (latest == null)
        {
            throw NotAllowed("WILL_NOT_SIGNED");
        }
        return await BuildDocumentAsync(vault, latest, userId, ct);
    }

    public async Task<ExecutorWillResponse> GetForInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User user = await RequireActiveUserAsync(userId, ct);
        Executor? invitation = await invitations.FindByIdAsync(invitationId, ct);
        if (invitation == null || !IsOpenInvitationFor(invitation, user, time.GetUtcNow()))
        {
            throw InvitationNotFound();
        }
        Vault? vault = await vaults.FindByIdAsync(invitation.VaultId, ct);
        if (vault == null || vault.OwnerId == userId)
        {
            throw InvitationNotFound();
        }
        WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        List<AssetAssignment> current = await assignments.FindByVaultIdAsync(vault.Id, ct);
        if (latest == null || !WillVersionRules.IsReadyForInvitee(vault.Plan, invitation, latest, current))
        {
            throw NotAllowed("WILL_NOT_SIGNED");
        }
        return await BuildDocumentAsync(vault, latest, userId, ct);
    }

    private async Task<User> RequireActiveUserAsync(Guid userId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản hiện không được phép thực hiện thao tác.");
        }
        return user;
    }

    /// <summary>Lời mời còn chờ, còn hạn và đúng là gửi cho tài khoản này (email đã xác minh).</summary>
    private static bool IsOpenInvitationFor(Executor invitation, User user, DateTimeOffset now)
    {
        if (invitation.Status != ExecutorStatus.Invited || invitation.ExpiresAt <= now)
        {
            return false;
        }
        if (invitation.UserId != null && invitation.UserId != user.Id)
        {
            return false;
        }
        if (user.EmailVerifiedAt == null || invitation.InviteeEmail == null)
        {
            return false;
        }
        return string.Equals(user.Email.Trim(), invitation.InviteeEmail.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAcceptedExecutor(Vault vault, Guid userId)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        foreach (Executor item in vault.Executors)
        {
            if (item.UserId == userId && item.Status == ExecutorStatus.Accepted)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Dựng tờ từ bản chụp đã ký và ghi nhật ký lần xem; không đưa danh tính hay tên tài sản vào nhật ký (SEC-07).</summary>
    private async Task<ExecutorWillResponse> BuildDocumentAsync(
        Vault vault, WillVersion version, Guid viewerId, CancellationToken ct)
    {
        WillSnapshot snapshot = WillVersionRules.FromJson(version.SnapshotJson);
        List<PlanSetupRecipientResponse> recipients = new List<PlanSetupRecipientResponse>();
        List<WillRecipientIdentityResponse> identities = new List<WillRecipientIdentityResponse>();
        await FillRecipientsAsync(vault.Id, snapshot, recipients, identities, ct);

        string ownerName = string.Empty;
        string ownerEmail = string.Empty;
        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        if (owner != null)
        {
            ownerName = owner.FullName;
            ownerEmail = owner.Email;
        }
        WillOwnerIdentityResponse? ownerIdentity = await ReadOwnerIdentityAsync(vault.OwnerId, ct);
        if (ownerIdentity != null && !string.IsNullOrWhiteSpace(ownerIdentity.FullName))
        {
            ownerName = ownerIdentity.FullName;
        }
        int unassigned = await CountOutsideSnapshotAsync(vault.Id, snapshot, ct);

        audit.Add(AuditRules.Create(vault.Id, viewerId, AuditActorRole.Executor, AuditAction.ExecutorWillViewed,
            AuditTargetType.Vault, vault.Id, version.VersionNo, AuditOutcome.Succeeded, null, time.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);
        return new ExecutorWillResponse(version.VersionNo, version.SignedAt, ownerName, ownerEmail, recipients,
            unassigned, new WillIdentityResponse(ownerIdentity, identities));
    }

    /// <summary>Null khi chưa có danh tính eKYC hoặc không giải mã được; các phần khác của tờ vẫn xem được.</summary>
    private async Task<WillOwnerIdentityResponse?> ReadOwnerIdentityAsync(Guid ownerId, CancellationToken ct)
    {
        OwnerIdentity? identity = await ownerIdentities.FindByUserIdAsync(ownerId, ct);
        if (identity == null)
        {
            return null;
        }
        try
        {
            OwnerIdentityData data = ownerProtector.Unprotect(identity);
            return new WillOwnerIdentityResponse(data.FullName, data.DateOfBirth, data.CitizenId, data.ResidenceAddress);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Người nhận và tài sản theo bản ký. Hồ sơ người nhận đã xóa hoặc không giải mã được thì vẫn liệt kê (tên rỗng,
    /// không có danh tính), không làm hỏng cả tờ.
    /// </summary>
    private async Task FillRecipientsAsync(Guid vaultId, WillSnapshot snapshot,
        List<PlanSetupRecipientResponse> recipients, List<WillRecipientIdentityResponse> identities, CancellationToken ct)
    {
        // Số người nhận của từng tài sản trên bản ký, để biết tài sản nào nhận chung.
        Dictionary<Guid, int> recipientCountByAsset = new Dictionary<Guid, int>();
        foreach (WillAssignmentPair pair in snapshot.Assignments)
        {
            if (recipientCountByAsset.ContainsKey(pair.AssetId))
            {
                recipientCountByAsset[pair.AssetId] = recipientCountByAsset[pair.AssetId] + 1;
            }
            else
            {
                recipientCountByAsset[pair.AssetId] = 1;
            }
        }

        Dictionary<Guid, PlanSetupRecipientResponse> byBeneficiary = new Dictionary<Guid, PlanSetupRecipientResponse>();
        foreach (WillAssignmentPair pair in snapshot.Assignments)
        {
            if (!byBeneficiary.ContainsKey(pair.BeneficiaryId))
            {
                string fullName = string.Empty;
                WillRecipientIdentityResponse? identity = await ReadRecipientIdentityAsync(vaultId, pair.BeneficiaryId, ct);
                if (identity != null)
                {
                    fullName = identity.FullName;
                    identities.Add(identity);
                }
                byBeneficiary[pair.BeneficiaryId] = new PlanSetupRecipientResponse(
                    pair.BeneficiaryId, fullName, false, new List<PlanSetupRecipientAssetResponse>());
            }
            // Tài sản đã xóa nội dung vẫn còn tên; chỉ khi mất hẳn dòng mới ghi câu thay thế.
            string assetName = "Tài sản đã bị xóa";
            string assetKind = AssetKind.File.ToApiString();
            Asset? asset = await assets.FindByIdAsync(vaultId, pair.AssetId, ct);
            if (asset != null)
            {
                assetName = asset.Name;
                assetKind = asset.Kind.ToApiString();
            }
            byBeneficiary[pair.BeneficiaryId].Assets.Add(new PlanSetupRecipientAssetResponse(
                pair.AssetId, assetName, assetKind, recipientCountByAsset[pair.AssetId]));
        }
        recipients.AddRange(byBeneficiary.Values);
        recipients.Sort(CompareByName);
    }

    private async Task<WillRecipientIdentityResponse?> ReadRecipientIdentityAsync(
        Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vaultId, beneficiaryId, ct);
        if (beneficiary == null)
        {
            return null;
        }
        try
        {
            BeneficiaryPersonalData data = beneficiaryProtector.Unprotect(beneficiary);
            return new WillRecipientIdentityResponse(beneficiaryId, data.FullName, data.DateOfBirth, data.CitizenId);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private async Task<int> CountOutsideSnapshotAsync(Guid vaultId, WillSnapshot snapshot, CancellationToken ct)
    {
        HashSet<Guid> signed = new HashSet<Guid>();
        foreach (WillAssignmentPair pair in snapshot.Assignments)
        {
            signed.Add(pair.AssetId);
        }
        int count = 0;
        List<Asset> current = await handoverRecords.ListVaultAssetsAsync(vaultId, ct);
        foreach (Asset asset in current)
        {
            if (!signed.Contains(asset.Id))
            {
                count++;
            }
        }
        return count;
    }

    private static int CompareByName(PlanSetupRecipientResponse left, PlanSetupRecipientResponse right)
    {
        return string.Compare(left.FullName, right.FullName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static AppException InvitationNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.ExecutorInvitationInvalid,
            "Lời mời Executor không hợp lệ hoặc đã hết hạn.");
    }

    private async Task<(Vault Vault, Executor Executor)> LoadAsync(
        Guid vaultId, Guid userId, RespondWillVersionRequest request, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        Executor? executor = null;
        if (vault != null && vault.OwnerId != userId)
        {
            foreach (Executor item in vault.Executors)
            {
                if (item.UserId == userId && item.Status == ExecutorStatus.Accepted)
                {
                    executor = item;
                }
            }
        }
        if (vault == null || executor == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        if (!PlanRules.IsRunning(vault.Plan.Status))
        {
            throw NotAllowed("PLAN_NOT_RUNNING");
        }
        WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        if (latest == null || latest.VersionNo != request.WillVersionNo)
        {
            throw NotAllowed("WILL_VERSION_CHANGED");
        }
        return (vault, executor);
    }

    private static AppException NotAllowed(string reason)
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed,
            "Không thể trả lời biên bản phân chia lúc này. Tải lại để xem bản mới nhất.",
            new Dictionary<string, object?> { ["reason"] = reason });
    }
}
