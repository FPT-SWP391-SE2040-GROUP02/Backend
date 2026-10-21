using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IPlanSetupService
{
    Task<PlanSetupResponse> GetAsync(Guid vaultId, CancellationToken ct);
    Task<ExecutorInvitationResponse> InviteAsync(Guid vaultId, InviteExecutorRequest request, CancellationToken ct);
    Task<ExecutorInvitationResponse> ResendAsync(Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct);
    Task<ExecutorInvitationInfo> InspectAsync(string token, CancellationToken ct);
    Task<ExecutorInvitationResponse> RespondAsync(RespondExecutorInvitationRequest request, CancellationToken ct);
    Task<PlanSetupResponse> ActivateAsync(Guid vaultId, ActivatePlanRequest request, CancellationToken ct);
}

/// <summary>Owner setup and explicit recipient acceptance. No identity/payment bypass and no implicit activation.</summary>
public sealed class PlanSetupService(ICurrentUser currentUser, IVaultRepository vaults,
    IUserRepository users, IContainerRepository containers, IBeneficiaryRepository beneficiaries,
    IEkycVerificationRepository ekyc, IExecutorInvitationRepository invitations, ITokenService tokens,
    IExecutorInvitationSender sender, ICheckInScheduler scheduler, IHoldReader holdReader, IUnitOfWork unitOfWork,
    TimeProvider time, IAssetRepository assets) : IPlanSetupService
{
    public const string PolicyVersion = "setup-2026-10-15";
    public const string ScopeVersion = "executor-2026-10-15";
    public const string ScopeText = "Executor theo dõi khi Owner quá hạn điểm danh và thực hiện nhiệm vụ kiểm tra/chứng tử theo phân công. Chấp nhận nhiệm vụ không cấp quyền đọc tài sản, duyệt thay danh tính hoặc tự nhận di sản.";
    // The immutable version identifies this text. Change the version whenever changing these warnings.
    public static readonly string PolicyText = $"Kích hoạt bắt đầu lịch điểm danh đã chọn. Quá hạn chỉ tạo bước kiểm tra, không tự bàn giao. Lời mời người nhận có hạn phản hồi {HandoverRules.ResponseWindow.TotalDays:0} ngày; khi đủ điều kiện, hạn quyết định {HandoverRules.DecisionWindow.TotalDays:0} ngày và quyền xem/tải {HandoverRules.GrantWindow.TotalDays:0} ngày từ lúc đồng ý. Không thao tác đúng hạn có thể mất quyền tiếp nhận/tải. Dữ liệu chỉ được xóa sau khi kiểm tra các điều kiện lưu giữ, tham chiếu và chặn liên quan; không mặc nhiên xóa ngay khi hết một lời mời. Chưa có Executor dự phòng độc lập có thể làm chậm xử lý khi người chính không thực hiện được nhiệm vụ.";

    public async Task<PlanSetupResponse> GetAsync(Guid vaultId, CancellationToken ct) =>
        await BuildAsync(await OwnedAsync(vaultId, ct), ct);

    public async Task<ExecutorInvitationResponse> InviteAsync(Guid vaultId, InviteExecutorRequest request, CancellationToken ct)
    {
        var token = tokens.CreateOpaqueToken();
        var executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var vault = await OwnedAsync(vaultId, ct);
            await EnsureInvitationEditableAsync(vault, ct);
            var role = request.Role == "PRIMARY" ? ExecutorRole.Primary : ExecutorRole.Backup;
            if (request.Role is not ("PRIMARY" or "BACKUP")) throw ValidationErrors.For("role", "Vai trò Executor không hợp lệ.");
            var email = request.Email.Trim().ToLowerInvariant();
            var owner = await users.FindByIdAsync(vault.OwnerId, ct);
            if (email == owner!.Email.Trim().ToLowerInvariant()) throw NotAllowed("OWNER_CANNOT_ACCEPT");
            var now = time.GetUtcNow();
            foreach (var previous in vault.Executors.Where(e => e.Role == role && e.Status == ExecutorStatus.Invited && e.ExpiresAt <= now))
            {
                previous.Status = ExecutorStatus.Expired;
                previous.EndedAt = now;
                previous.UpdatedAt = now;
            }
            Guid? replaces = null;
            var occupying = vault.Executors.Where(e => e.Role == role
                && e.Status is ExecutorStatus.Invited or ExecutorStatus.Accepted or ExecutorStatus.Suspended).ToList();
            if (occupying.Count > 0)
            {
                // SETUP-06: kế hoạch đang chạy mà đã có Executor chính đã chấp nhận → mời người thay; người cũ giữ vai trò
                // cho tới khi người mới chấp nhận. Mọi trường hợp khác vẫn là trùng lời mời.
                bool replacement = PlanRules.IsRunning(vault.Plan.Status) && role == ExecutorRole.Primary
                    && occupying.Count == 1 && occupying[0].Status == ExecutorStatus.Accepted;
                if (!replacement) throw Conflict();
                replaces = occupying[0].Id;
            }
            if (request.BeneficiaryId is Guid beneficiaryId && await beneficiaries.FindByIdAsync(vault.Id, beneficiaryId, ct) is null)
                throw new AppException(AppErrorType.NotFound, ErrorCode.BeneficiaryNotFound, "Không tìm thấy hồ sơ người nhận.");
            var item = new Executor
            {
                VaultId = vault.Id, Role = role, Status = ExecutorStatus.Invited,
                InviteeEmail = email, InvitationTokenHash = token.Hash,
                IsBeneficiary = request.BeneficiaryId.HasValue, BeneficiaryId = request.BeneficiaryId,
                InvitedAt = now, ExpiresAt = now.AddDays(7),
                ReplacesExecutorId = replaces,
            };
            invitations.Add(item);
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        return await DeliverAsync(executor, token.Value, ct);
    }

    public async Task<ExecutorInvitationResponse> ResendAsync(Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct)
    {
        var token = tokens.CreateOpaqueToken();
        var executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var vault = await OwnedAsync(vaultId, ct);
            await EnsureInvitationEditableAsync(vault, ct);
            var item = vault.Executors.SingleOrDefault(e => e.Id == executorId) ?? throw InvalidInvitation();
            if (!Etag.Matches(request.Etag, item.UpdatedAt)) throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict, "Lời mời đã thay đổi. Tải lại trước khi gửi tiếp.");
            if (item.Status is not (ExecutorStatus.Invited or ExecutorStatus.Expired or ExecutorStatus.Suspended)
                && !(item.Status == ExecutorStatus.Accepted && item.AcceptedScopeVersion != ScopeVersion)) throw NotAllowed("WRONG_STATE");
            var now = time.GetUtcNow();
            item.Status = ExecutorStatus.Invited;
            item.InvitationTokenHash = token.Hash;
            item.InvitedAt = now;
            item.ExpiresAt = now.AddDays(7);
            item.InvitationSentAt = null;
            item.AcceptedAt = null;
            item.AcceptedScopeVersion = null;
            item.EndedAt = null;
            item.UpdatedAt = now;
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        return await DeliverAsync(executor, token.Value, ct);
    }

    public async Task<ExecutorInvitationInfo> InspectAsync(string token, CancellationToken ct)
    {
        var item = await FindTokenAsync(token, ct);
        if (item.Status != ExecutorStatus.Accepted && (item.Status != ExecutorStatus.Invited || item.ExpiresAt <= time.GetUtcNow())) throw InvalidInvitation();
        return new(item.Role.ToApiString(), item.ExpiresAt, item.Status.ToApiString(), ScopeVersion, ScopeText);
    }

    public Task<ExecutorInvitationResponse> RespondAsync(RespondExecutorInvitationRequest request, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var userId = currentUser.RequireUserId();
            var responder = await UsableUserAsync(userId, ct);
            if (request.ScopeVersion != ScopeVersion || (request.Accept && !request.AcknowledgeScope))
                throw new AppException(AppErrorType.Validation, ErrorCode.PolicyAcknowledgementRequired, "Cần đọc và xác nhận phạm vi nhiệm vụ hiện hành.");
            var item = await FindTokenAsync(request.Token, ct);
            var vault = await vaults.FindByIdAsync(item.VaultId, ct) ?? throw InvalidInvitation();
            if (vault.OwnerId == userId) throw NotAllowed("OWNER_CANNOT_ACCEPT");
            if (item.UserId.HasValue && item.UserId != userId) throw Conflict();
            if (item.UserId == userId && ((request.Accept && item.Status == ExecutorStatus.Accepted) || (!request.Accept && item.Status == ExecutorStatus.Declined)))
                return Map(item);
            if (item.Status != ExecutorStatus.Invited || item.ExpiresAt <= time.GetUtcNow()) throw InvalidInvitation();
            // M4, SETUP-20: người trả lời phải dùng tài khoản có email đã xác minh trùng email được mời; link bị chuyển tiếp
            // hay tài khoản email khác không nhận được vai trò Executor.
            if (!string.Equals(responder.Email?.Trim(), item.InviteeEmail?.Trim(), StringComparison.OrdinalIgnoreCase)) throw NotAllowed("INVITEE_EMAIL_MISMATCH");
            VaultRules.EnsureEditable(vault);
            if (vault.Plan.Status != PlanStatus.Draft && !PlanRules.IsRunning(vault.Plan.Status)) throw NotAllowed("PLAN_NOT_DRAFT");
            if (request.Accept && vault.Executors.Any(e => e.Id != item.Id && e.UserId == userId && e.Status == ExecutorStatus.Accepted)) throw NotAllowed("INDEPENDENT_EXECUTOR_REQUIRED");
            var now = time.GetUtcNow();
            item.UserId = userId;
            item.Status = request.Accept ? ExecutorStatus.Accepted : ExecutorStatus.Declined;
            item.AcceptedAt = request.Accept ? now : null;
            item.AcceptedScopeVersion = request.Accept ? ScopeVersion : null;
            item.EndedAt = request.Accept ? null : now;
            item.UpdatedAt = now;
            if (request.Accept && item.ReplacesExecutorId is Guid replacedId)
            {
                // SETUP-06: người thay chấp nhận thì người cũ thôi vai trò trong cùng giao dịch.
                var replaced = vault.Executors.SingleOrDefault(e => e.Id == replacedId && e.Status == ExecutorStatus.Accepted);
                if (replaced is not null)
                {
                    replaced.Status = ExecutorStatus.Revoked;
                    replaced.EndedAt = now;
                    replaced.UpdatedAt = now;
                }
            }
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return Map(item);
        }, ct);

    public Task<PlanSetupResponse> ActivateAsync(Guid vaultId, ActivatePlanRequest request, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var vault = await OwnedAsync(vaultId, ct);
            await UsableUserAsync(vault.OwnerId, ct);
            if (!request.AcknowledgePolicy || request.PolicyVersion != PolicyVersion)
                throw new AppException(AppErrorType.Validation, ErrorCode.PolicyAcknowledgementRequired, "Cần đọc và xác nhận chính sách hiện hành.");
            if (request.ConfigVersion != vault.Plan.ConfigVersion)
                throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict, "Cấu hình kế hoạch đã thay đổi. Tải lại trước khi kích hoạt.");
            // A duplicate request returns the existing state without resetting deadlines or opening a period.
            if (vault.Plan.Status == PlanStatus.Active) return await BuildAsync(vault, ct);
            if (vault.Plan.Status != PlanStatus.Draft)
                throw new AppException(AppErrorType.Conflict, ErrorCode.PlanActivationNotAllowed, "Trạng thái kế hoạch không cho phép kích hoạt lại.");
            var setup = await BuildAsync(vault, ct);
            if (!setup.CanActivate)
                throw new AppException(AppErrorType.Conflict, ErrorCode.PlanActivationRequirementsNotMet,
                    "Kế hoạch chưa đủ điều kiện kích hoạt.", new Dictionary<string, object?> { ["missingRequirements"] = setup.MissingRequirements });
            var now = time.GetUtcNow();
            vault.Plan.Status = PlanStatus.Active;
            vault.Plan.ActivatedAt = now;
            vault.Plan.ActivationPolicyVersion = PolicyVersion;
            vault.Plan.ActivationPolicyAcknowledgedAt = now;
            vault.Plan.UpdatedAt = now;
            await scheduler.StartFirstPeriodAsync(vault.Plan, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return await BuildAsync(vault, ct);
        }, ct);

    private async Task<Vault> OwnedAsync(Guid id, CancellationToken ct) =>
        VaultRules.EnsureOwnedBy(await vaults.FindByIdAsync(id, ct), currentUser.RequireUserId());

    private async Task<User> UsableUserAsync(Guid id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id, ct) ?? throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired, "Phiên đã hết hạn.");
        if (user.Status != UserStatus.Active) throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản hiện không được phép thực hiện thao tác.");
        if (user.EmailVerifiedAt is null) throw new AppException(AppErrorType.Forbidden, ErrorCode.EmailNotVerified, "Cần xác minh email trước khi tiếp tục.");
        return user;
    }

    private async Task EnsureInvitationEditableAsync(Vault vault, CancellationToken ct)
    {
        await UsableUserAsync(vault.OwnerId, ct);
        VaultRules.EnsureEditable(vault);
        if (vault.Plan.Status != PlanStatus.Draft && !PlanRules.IsRunning(vault.Plan.Status)) throw NotAllowed("PLAN_NOT_DRAFT");
        if (!Paid(vault)) throw new AppException(AppErrorType.Forbidden, ErrorCode.PaidTierRequired, "Cần gói trả phí còn hiệu lực.");
    }

    private bool Paid(Vault vault) => vault.Entitlement.Tier != ServiceTier.Free
        && vault.Entitlement.Status == EntitlementStatus.PaidActive
        && vault.Entitlement.ServiceExpiresAt > time.GetUtcNow();

    private async Task<PlanSetupResponse> BuildAsync(Vault vault, CancellationToken ct)
    {
        var missing = new List<string>();
        var user = await users.FindByIdAsync(vault.OwnerId, ct);
        if (user?.Status != UserStatus.Active) missing.Add("ACCOUNT_NOT_ACTIVE");
        if (user?.EmailVerifiedAt is null) missing.Add("EMAIL_VERIFICATION_REQUIRED");
        if (vault.Status != VaultStatus.Open) missing.Add("VAULT_NOT_OPEN");
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct)) missing.Add("HOLD_ACTIVE");
        if (!Paid(vault)) missing.Add("PAID_SERVICE_REQUIRED");
        // EKYC-01: chỉ lượt eKYC đăng ký Owner. Đã từng đạt thì giữ, chụp lại giữa chừng không làm mất kết quả.
        if (await ekyc.FindLatestPassedByUserAndPurposeAsync(vault.OwnerId, EkycPurpose.OwnerEnrollment, ct) == null) missing.Add("OWNER_EKYC_REQUIRED");
        if (await containers.CountReadyByVaultIdAsync(vault.Id, ct) == 0) missing.Add("READY_CONTAINER_REQUIRED");
        var accepted = new List<Executor>();
        foreach (var item in vault.Executors.Where(e => e.Status == ExecutorStatus.Accepted && e.UserId.HasValue && e.AcceptedAt.HasValue && e.AcceptedScopeVersion == ScopeVersion))
        {
            var executorUser = await users.FindByIdAsync(item.UserId!.Value, ct);
            if (executorUser?.Status == UserStatus.Active && executorUser.EmailVerifiedAt.HasValue && executorUser.Id != vault.OwnerId) accepted.Add(item);
        }
        var primaries = accepted.Where(e => e.Role == ExecutorRole.Primary).ToList();
        var primary = primaries.Count == 1 ? primaries[0] : null;
        if (primary is null) missing.Add("PRIMARY_EXECUTOR_REQUIRED");
        if (primary?.IsBeneficiary == true && !accepted.Any(e => e.Role == ExecutorRole.Backup && !e.IsBeneficiary && e.UserId != primary.UserId)) missing.Add("BACKUP_EXECUTOR_REQUIRED");
        if (vault.Plan.Status != PlanStatus.Draft) missing.Add("PLAN_NOT_DRAFT");
        var openContainers = await containers.FindOpenByVaultIdAsync(vault.Id, ct);
        var outsideAssets = await assets.FindOutsideContainerByVaultIdAsync(vault.Id, ct);
        var warnings = PlanSetupWarningRules.GetWarnings(accepted);
        return new(vault.Plan.Status.ToApiString(), vault.Plan.ConfigVersion, missing.Count == 0,
            missing.ToArray(), PolicyVersion, PolicyText, ScopeVersion, ScopeText,
            vault.Executors.OrderByDescending(e => e.InvitedAt).Select(Map).ToList(),
            PlanSetupMappings.ToContainerItems(openContainers), PlanSetupMappings.ToAssetItems(outsideAssets), warnings.ToArray());
    }

    private async Task<Executor> FindTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 512) throw InvalidInvitation();
        return await invitations.FindByTokenHashAsync(tokens.HashOpaqueToken(token), ct) ?? throw InvalidInvitation();
    }

    private async Task<ExecutorInvitationResponse> DeliverAsync(Executor executor, string token, CancellationToken ct)
    {
        // External delivery follows the committed invitation. Failed delivery never grants access.
        if (await sender.SendAsync(executor.InviteeEmail, executor.Role.ToApiString(), token, executor.ExpiresAt, ct))
        {
            executor.InvitationSentAt = time.GetUtcNow();
            executor.UpdatedAt = time.GetUtcNow();
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Map(executor);
    }
    private ExecutorInvitationResponse Map(Executor item) => new(item.Id, item.Role.ToApiString(),
        item.Status == ExecutorStatus.Invited && item.ExpiresAt <= time.GetUtcNow() ? "EXPIRED" : item.Status.ToApiString(), item.InviteeEmail,
        item.IsBeneficiary, item.ExpiresAt, item.AcceptedAt, item.InvitationSentAt, item.AcceptedScopeVersion, Etag.From(item.UpdatedAt));
    private static AppException InvalidInvitation() => new(AppErrorType.NotFound, ErrorCode.ExecutorInvitationInvalid, "Lời mời Executor không hợp lệ hoặc đã hết hạn.");
    private static AppException Conflict() => new(AppErrorType.Conflict, ErrorCode.ExecutorInvitationConflict, "Lời mời hoặc phân công Executor đã tồn tại/thay đổi. Tải lại để kiểm tra.");
    private static AppException NotAllowed(string reason) => new(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed, "Không thể thực hiện thao tác Executor lúc này.", new Dictionary<string, object?> { ["reason"] = reason });
}
