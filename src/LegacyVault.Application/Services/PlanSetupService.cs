using LegacyVault.Application.Dtos;
using LegacyVault.Application.Dtos.Responses;
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

    /// <summary>Lời mời gửi tới email đã xác minh của tài khoản đang đăng nhập; email chưa xác minh thì danh sách rỗng.</summary>
    Task<MyExecutorInvitationsResponse> ListMyInvitationsAsync(CancellationToken ct);

    /// <summary>
    /// Trả lời lời mời theo id thay cho token trong link email; cùng luật như RespondAsync.
    /// Lời mời không gửi tới email của người gọi → 404 như lời mời không tồn tại.
    /// </summary>
    Task<ExecutorInvitationResponse> RespondByIdAsync(Guid invitationId, RespondMyExecutorInvitationRequest request, CancellationToken ct);
    Task<PlanSetupResponse> ActivateAsync(Guid vaultId, ActivatePlanRequest request, CancellationToken ct);
    Task<bool> CheckEmailExistsAsync(string email, CancellationToken ct);
}

/// <summary>Owner setup and explicit recipient acceptance. No identity/payment bypass and no implicit activation.</summary>
public sealed class PlanSetupService(ICurrentUser currentUser, IVaultRepository vaults,
    IUserRepository users, IContainerRepository containers, IBeneficiaryRepository beneficiaries,
    IEkycVerificationRepository ekyc, IExecutorInvitationRepository invitations, ITokenService tokens,
    IExecutorInvitationSender sender, ICheckInScheduler scheduler, IHoldReader holdReader, IUnitOfWork unitOfWork,
    TimeProvider time, IAssetRepository assets) : IPlanSetupService
{
    public const string PolicyVersion = "setup-2026-10-30";
    public const string ScopeVersion = "executor-2026-10-15";
    public const string ScopeText = "Executor theo dõi khi Owner quá hạn điểm danh và thực hiện nhiệm vụ kiểm tra/chứng tử theo phân công. Chấp nhận nhiệm vụ không cấp quyền đọc tài sản, duyệt thay danh tính hoặc tự nhận di sản.";
    // The immutable version identifies this text. Change the version whenever changing these warnings.
    public static string PolicyText => $"Kích hoạt bắt đầu lịch điểm danh đã chọn. Quá hạn chỉ tạo bước kiểm tra, không tự bàn giao. Lời mời người nhận có hạn phản hồi {HandoverRules.ResponseWindow.TotalDays:0} ngày; khi đủ điều kiện, hạn quyết định {HandoverRules.DecisionWindow.TotalDays:0} ngày và quyền xem/tải {HandoverRules.GrantWindow.TotalDays:0} ngày từ lúc đồng ý. Không thao tác đúng hạn có thể mất quyền tiếp nhận/tải. Dữ liệu chỉ được xóa sau khi kiểm tra các điều kiện lưu giữ, tham chiếu và chặn liên quan; không mặc nhiên xóa ngay khi hết một lời mời. Nếu Executor không thực hiện được nhiệm vụ, Chủ kho cần mời người thay để kế hoạch không bị chậm.";

    public async Task<bool> CheckEmailExistsAsync(string email, CancellationToken ct)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return await users.FindByEmailAsync(normalized, ct) is not null;
    }

    public async Task<PlanSetupResponse> GetAsync(Guid vaultId, CancellationToken ct) =>
        await BuildAsync(await OwnedAsync(vaultId, ct), ct);

    public async Task<ExecutorInvitationResponse> InviteAsync(Guid vaultId, InviteExecutorRequest request, CancellationToken ct)
    {
        var token = tokens.CreateOpaqueToken();
        bool accountExists = false;
        var executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var vault = await OwnedAsync(vaultId, ct);
            await EnsureInvitationEditableAsync(vault, ct);
            var email = request.Email.Trim().ToLowerInvariant();
            var owner = await users.FindByIdAsync(vault.OwnerId, ct);
            if (email == owner!.Email.Trim().ToLowerInvariant()) throw NotAllowed("OWNER_CANNOT_ACCEPT");
            var inviteeUser = await users.FindByEmailAsync(email, ct);
            accountExists = inviteeUser is not null;
            var now = time.GetUtcNow();
            foreach (var previous in vault.Executors.Where(e => e.Status == ExecutorStatus.Invited && e.ExpiresAt <= now))
            {
                previous.Status = ExecutorStatus.Expired;
                previous.EndedAt = now;
                previous.UpdatedAt = now;
            }
            Guid? replaces = null;
            var occupying = vault.Executors.Where(e => e.Status is ExecutorStatus.Invited or ExecutorStatus.Accepted or ExecutorStatus.Suspended).ToList();
            if (occupying.Count > 0)
            {
                // SETUP-06: kế hoạch đang chạy mà đã có Executor đã chấp nhận → mời người thay; người cũ giữ vai trò
                // cho tới khi người mới chấp nhận. Mọi trường hợp khác vẫn là trùng lời mời.
                bool replacement = PlanRules.IsRunning(vault.Plan.Status)
                    && occupying.Count == 1 && occupying[0].Status == ExecutorStatus.Accepted;
                if (!replacement) throw Conflict();
                replaces = occupying[0].Id;
            }
            if (request.BeneficiaryId is Guid beneficiaryId && await beneficiaries.FindByIdAsync(vault.Id, beneficiaryId, ct) is null)
                throw new AppException(AppErrorType.NotFound, ErrorCode.BeneficiaryNotFound, "Không tìm thấy hồ sơ người nhận.");
            var item = new Executor
            {
                VaultId = vault.Id, Status = ExecutorStatus.Invited,
                InviteeEmail = email, InvitationTokenHash = token.Hash,
                IsBeneficiary = request.BeneficiaryId.HasValue, BeneficiaryId = request.BeneficiaryId,
                InvitedAt = now, ExpiresAt = now.AddDays(RuntimeSettings.Int("Executor.InvitationExpiresDays", 7)),
                ReplacesExecutorId = replaces,
            };
            invitations.Add(item);
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        return await DeliverAsync(executor, token.Value, accountExists, ct);
    }

    public async Task<ExecutorInvitationResponse> ResendAsync(Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct)
    {
        var token = tokens.CreateOpaqueToken();
        bool accountExists = false;
        var executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var vault = await OwnedAsync(vaultId, ct);
            await EnsureInvitationEditableAsync(vault, ct);
            var item = vault.Executors.SingleOrDefault(e => e.Id == executorId) ?? throw InvalidInvitation();
            if (!Etag.Matches(request.Etag, item.UpdatedAt)) throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict, "Lời mời đã thay đổi. Tải lại trước khi gửi tiếp.");
            if (item.Status is not (ExecutorStatus.Invited or ExecutorStatus.Expired or ExecutorStatus.Suspended)
                && !(item.Status == ExecutorStatus.Accepted && item.AcceptedScopeVersion != ScopeVersion)) throw NotAllowed("WRONG_STATE");
            var inviteeUser = await users.FindByEmailAsync(item.InviteeEmail, ct);
            accountExists = inviteeUser is not null;
            var now = time.GetUtcNow();
            item.Status = ExecutorStatus.Invited;
            item.InvitationTokenHash = token.Hash;
            item.InvitedAt = now;
            item.ExpiresAt = now.AddDays(RuntimeSettings.Int("Executor.InvitationExpiresDays", 7));
            item.InvitationSentAt = null;
            item.AcceptedAt = null;
            item.AcceptedScopeVersion = null;
            item.EndedAt = null;
            item.UpdatedAt = now;
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        return await DeliverAsync(executor, token.Value, accountExists, ct);
    }

    public async Task<ExecutorInvitationInfo> InspectAsync(string token, CancellationToken ct)
    {
        var item = await FindTokenAsync(token, ct);
        if (item.Status != ExecutorStatus.Accepted && (item.Status != ExecutorStatus.Invited || item.ExpiresAt <= time.GetUtcNow())) throw InvalidInvitation();
        return new(item.ExpiresAt, item.Status.ToApiString(), ScopeVersion, ScopeText);
    }

    public Task<ExecutorInvitationResponse> RespondAsync(RespondExecutorInvitationRequest request, CancellationToken ct)
    {
        return RespondCoreAsync(request.Accept, request.AcknowledgeScope, request.ScopeVersion,
            responder => FindTokenAsync(request.Token, ct), ct);
    }

    public Task<ExecutorInvitationResponse> RespondByIdAsync(
        Guid invitationId, RespondMyExecutorInvitationRequest request, CancellationToken ct)
    {
        return RespondCoreAsync(request.Accept, request.AcknowledgeScope, request.ScopeVersion,
            responder => FindMyInvitationAsync(invitationId, responder, ct), ct);
    }

    public async Task<MyExecutorInvitationsResponse> ListMyInvitationsAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        MyExecutorInvitationsResponse response = new MyExecutorInvitationsResponse();
        response.ScopeVersion = ScopeVersion;
        response.ScopeText = ScopeText;

        User? user = await users.FindByIdAsync(userId, ct);
        // Email chưa xác minh thì chưa chứng minh được là chủ email đó: không hiện lời mời gửi tới email này.
        if (user == null || user.Status != UserStatus.Active || user.EmailVerifiedAt == null)
        {
            return response;
        }

        string email = user.Email.Trim().ToLowerInvariant();
        DateTimeOffset now = time.GetUtcNow();
        List<Executor> items = await invitations.ListByInviteeEmailAsync(email, MyInvitationsMaxCount, ct);
        foreach (Executor item in items)
        {
            // Chủ kho không nhận lời mời của chính kho mình; lời mời đã nhận nằm ở danh sách kho của Executor.
            if (item.Vault.OwnerId == userId)
            {
                continue;
            }

            bool expired = item.Status == ExecutorStatus.Invited && item.ExpiresAt <= now;
            if (item.Status == ExecutorStatus.Invited && !expired)
            {
                response.Pending.Add(ToMyInvitation(item, "INVITED", null));
            }
            else if (expired)
            {
                response.Ended.Add(ToMyInvitation(item, "EXPIRED", item.ExpiresAt));
            }
            else if (item.Status == ExecutorStatus.Declined
                || item.Status == ExecutorStatus.Expired
                || item.Status == ExecutorStatus.Revoked)
            {
                response.Ended.Add(ToMyInvitation(item, item.Status.ToApiString(), item.EndedAt));
            }
        }

        // Lời mời sắp hết hạn lên trước để người dùng trả lời kịp.
        response.Pending.Sort(CompareByExpiresAt);
        return response;
    }

    private Task<ExecutorInvitationResponse> RespondCoreAsync(bool accept, bool acknowledgeScope, string scopeVersion,
        Func<User, Task<Executor>> findInvitation, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async () =>
        {
            var userId = currentUser.RequireUserId();
            var responder = await UsableUserAsync(userId, ct);
            if (scopeVersion != ScopeVersion || (accept && !acknowledgeScope))
                throw new AppException(AppErrorType.Validation, ErrorCode.PolicyAcknowledgementRequired, "Cần đọc và xác nhận phạm vi nhiệm vụ hiện hành.");
            var item = await findInvitation(responder);
            var vault = await vaults.FindByIdAsync(item.VaultId, ct) ?? throw InvalidInvitation();
            if (vault.OwnerId == userId) throw NotAllowed("OWNER_CANNOT_ACCEPT");
            if (item.UserId.HasValue && item.UserId != userId) throw Conflict();
            if (item.UserId == userId && ((accept && item.Status == ExecutorStatus.Accepted) || (!accept && item.Status == ExecutorStatus.Declined)))
                return Map(item);
            if (item.Status != ExecutorStatus.Invited || item.ExpiresAt <= time.GetUtcNow()) throw InvalidInvitation();
            // M4, SETUP-20: người trả lời phải dùng tài khoản có email đã xác minh trùng email được mời; link bị chuyển tiếp
            // hay tài khoản email khác không nhận được vai trò Executor.
            if (!string.Equals(responder.Email?.Trim(), item.InviteeEmail?.Trim(), StringComparison.OrdinalIgnoreCase)) throw NotAllowed("INVITEE_EMAIL_MISMATCH");
            VaultRules.EnsureEditable(vault);
            if (vault.Plan.Status != PlanStatus.Draft && !PlanRules.IsRunning(vault.Plan.Status)) throw NotAllowed("PLAN_NOT_DRAFT");
            if (accept && vault.Executors.Any(e => e.Id != item.Id && e.UserId == userId && e.Status == ExecutorStatus.Accepted)) throw NotAllowed("INDEPENDENT_EXECUTOR_REQUIRED");
            var now = time.GetUtcNow();
            item.UserId = userId;
            item.Status = accept ? ExecutorStatus.Accepted : ExecutorStatus.Declined;
            item.AcceptedAt = accept ? now : null;
            item.AcceptedScopeVersion = accept ? ScopeVersion : null;
            item.EndedAt = accept ? null : now;
            item.UpdatedAt = now;
            if (accept && item.ReplacesExecutorId is Guid replacedId)
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
        // Mỗi kho đúng một Executor đã chấp nhận (không còn Executor dự phòng).
        if (accepted.Count != 1) missing.Add("PRIMARY_EXECUTOR_REQUIRED");
        if (vault.Plan.Status != PlanStatus.Draft) missing.Add("PLAN_NOT_DRAFT");
        var openContainers = await containers.FindOpenByVaultIdAsync(vault.Id, ct);
        var outsideAssets = await assets.FindOutsideContainerByVaultIdAsync(vault.Id, ct);
        // Cảnh báo duy nhất trước đây là NO_BACKUP_EXECUTOR; đã bỏ Executor dự phòng nên giữ mảng rỗng cho đúng hợp đồng API.
        return new(vault.Plan.Status.ToApiString(), vault.Plan.ConfigVersion, missing.Count == 0,
            missing.ToArray(), PolicyVersion, PolicyText, ScopeVersion, ScopeText,
            vault.Executors.OrderByDescending(e => e.InvitedAt).Select(Map).ToList(),
            PlanSetupMappings.ToContainerItems(openContainers), PlanSetupMappings.ToAssetItems(outsideAssets), Array.Empty<string>());
    }

    private const int MyInvitationsMaxCount = 50;

    /// <summary>Lời mời theo id, chỉ khi gửi tới đúng email của người gọi; khác email thì coi như không tồn tại.</summary>
    private async Task<Executor> FindMyInvitationAsync(Guid invitationId, User responder, CancellationToken ct)
    {
        Executor? item = await invitations.FindByIdAsync(invitationId, ct);
        if (item == null)
        {
            throw InvalidInvitation();
        }
        string responderEmail = responder.Email.Trim().ToLowerInvariant();
        string inviteeEmail = item.InviteeEmail.Trim().ToLowerInvariant();
        if (responderEmail != inviteeEmail)
        {
            throw InvalidInvitation();
        }
        return item;
    }

    private static MyExecutorInvitationResponse ToMyInvitation(Executor item, string status, DateTimeOffset? endedAt)
    {
        MyExecutorInvitationResponse response = new MyExecutorInvitationResponse();
        response.Id = item.Id;
        response.OwnerName = item.Vault.Owner.FullName;
        response.InviteeEmail = item.InviteeEmail;
        response.Status = status;
        response.InvitedAt = item.InvitedAt;
        response.ExpiresAt = item.ExpiresAt;
        response.EndedAt = endedAt;
        return response;
    }

    private static int CompareByExpiresAt(MyExecutorInvitationResponse left, MyExecutorInvitationResponse right)
    {
        return left.ExpiresAt.CompareTo(right.ExpiresAt);
    }

    private async Task<Executor> FindTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 512) throw InvalidInvitation();
        return await invitations.FindByTokenHashAsync(tokens.HashOpaqueToken(token), ct) ?? throw InvalidInvitation();
    }

    private async Task<ExecutorInvitationResponse> DeliverAsync(Executor executor, string token, bool accountExists, CancellationToken ct)
    {
        // External delivery follows the committed invitation. Failed delivery never grants access.
        if (await sender.SendAsync(executor.InviteeEmail, token, executor.ExpiresAt, ct))
        {
            executor.InvitationSentAt = time.GetUtcNow();
            executor.UpdatedAt = time.GetUtcNow();
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Map(executor, accountExists);
    }
    private ExecutorInvitationResponse Map(Executor item) => Map(item, false);
    private ExecutorInvitationResponse Map(Executor item, bool accountExists) => new(item.Id,
        item.Status == ExecutorStatus.Invited && item.ExpiresAt <= time.GetUtcNow() ? "EXPIRED" : item.Status.ToApiString(), item.InviteeEmail,
        item.IsBeneficiary, item.ExpiresAt, item.AcceptedAt, item.InvitationSentAt, item.AcceptedScopeVersion, Etag.From(item.UpdatedAt), accountExists);
    private static AppException InvalidInvitation() => new(AppErrorType.NotFound, ErrorCode.ExecutorInvitationInvalid, "Lời mời Executor không hợp lệ hoặc đã hết hạn.");
    private static AppException Conflict() => new(AppErrorType.Conflict, ErrorCode.ExecutorInvitationConflict, "Lời mời hoặc phân công Executor đã tồn tại/thay đổi. Tải lại để kiểm tra.");
    private static AppException NotAllowed(string reason) => new(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed, "Không thể thực hiện thao tác Executor lúc này.", new Dictionary<string, object?> { ["reason"] = reason });
}
