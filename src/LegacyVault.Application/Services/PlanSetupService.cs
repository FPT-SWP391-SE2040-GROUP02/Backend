using LegacyVault.Application.Dtos;
using LegacyVault.Application.Dtos.Requests;
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

    /// <summary>
    /// Chủ tài sản ký lại di chúc (PIN kiểm ở filter) khi kế hoạch đang chạy và có thay đổi chưa ký: tạo phiên bản di chúc mới.
    /// Không có thay đổi thì trả nguyên trạng, không tạo phiên bản. configVersion cũ → 409 EDIT_CONFLICT.
    /// </summary>
    Task<PlanSetupResponse> SignWillAsync(Guid vaultId, SignWillRequest request, CancellationToken ct);
    Task<bool> CheckEmailExistsAsync(string email, CancellationToken ct);

    /// <summary>
    /// Chủ tài sản tìm tài khoản có sẵn để chọn làm Người thực thi. Từ khóa dưới 2 ký tự trả danh sách rỗng
    /// (không cho duyệt toàn bộ người dùng); tối đa 8 kết quả; không gồm chính Chủ tài sản, Admin, Verifier.
    /// </summary>
    Task<IReadOnlyList<ExecutorCandidateResponse>> SearchCandidatesAsync(Guid vaultId, string? query, CancellationToken ct);

    /// <summary>
    /// Admin giao hoặc đổi Người thực thi của kho (lý do bắt buộc, có nhật ký). Người được giao nhận lời mời qua email và phải
    /// chấp nhận; người cũ (nếu kế hoạch đang chạy) giữ vai trò tới khi người mới chấp nhận. Kho đang bàn giao thì người mới
    /// nhận việc ngay (ACCEPTED), người cũ thôi vai trò cùng giao dịch, báo thư người mới, người cũ và luật sư. Không phải Admin → 404.
    /// </summary>
    Task<ExecutorInvitationResponse> AssignByAdminAsync(Guid vaultId, AssignExecutorRequest request, CancellationToken ct);
}

/// <summary>Owner setup and explicit recipient acceptance. No identity/payment bypass and no implicit activation.</summary>
public sealed class PlanSetupService(ICurrentUser currentUser, IVaultRepository vaults,
    IUserRepository users, IAssetAssignmentRepository assignments, IBeneficiaryRepository beneficiaries,
    IEkycVerificationRepository ekyc, IExecutorInvitationRepository invitations, ITokenService tokens,
    IExecutorInvitationSender sender, IUnitOfWork unitOfWork,
    TimeProvider time, IAssetRepository assets, IPlanActivatedNotifier activatedNotifier, IAuditLogRepository audit,
    ExecutorKeyIssuer keyIssuer, IBeneficiaryDataProtector beneficiaryProtector,
    ISigningPasswordChecker signingPasswordChecker, IEmailBounceStore bounces,
    IWillVersionRepository willVersions, IHandoverRecordRepository handoverRecords,
    IHandoverRequestNoticeSender handoverNotices, IExecutorWillNoticeSender willNotices) : IPlanSetupService
{
    public const string PolicyVersion = "setup-2026-11-20";
    public const string ScopeVersion = "executor-2026-11-23";
    public const string ScopeText = "Người thực thi chỉ giữ hộ tài sản và giao theo biên bản bàn giao do luật sư lập: ký biên bản, gửi mã nhận cho người nhận. Không được tự chia, bán hay cho tài sản, không đọc nội dung tài sản và không làm thủ tục pháp lý.";
    // The immutable version identifies this text. Change the version whenever changing these warnings.
    public static string PolicyText => $"Hệ thống chỉ giữ và giao tài sản, không làm thủ tục pháp lý. Người thực thi bạn chọn chỉ là đề cử: họ chỉ giữ hộ tài sản và giao theo biên bản bàn giao do luật sư lập, không được tự chia, bán hay cho tài sản. Khi cần bàn giao, Người thực thi mở bàn giao, luật sư lập biên bản, kiểm danh tính người nhận, rồi ba bên ký. Lời mời người nhận có hạn phản hồi {HandoverRules.ResponseWindow.TotalDays:0} ngày; quyền xem/tải {HandoverRules.GrantWindow.TotalDays:0} ngày từ lúc biên bản đủ chữ ký. Dữ liệu chỉ được xóa sau khi kiểm tra các điều kiện lưu giữ, tham chiếu và chặn liên quan.";

    public async Task<bool> CheckEmailExistsAsync(string email, CancellationToken ct)
    {
        string normalized = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }
        return await users.FindByEmailAsync(normalized, ct) is not null;
    }

    public async Task<IReadOnlyList<ExecutorCandidateResponse>> SearchCandidatesAsync(
        Guid vaultId, string? query, CancellationToken ct)
    {
        Vault vault = await OwnedAsync(vaultId, ct);
        string term = (query ?? "").Trim().ToLowerInvariant();
        if (term.Length < 2)
        {
            return new List<ExecutorCandidateResponse>();
        }
        IReadOnlyList<User> found = await users.SearchExecutorCandidatesAsync(term, vault.OwnerId, 8, ct);
        return found.Select(u => new ExecutorCandidateResponse(u.Id, u.FullName, u.Email)).ToList();
    }

    public async Task<PlanSetupResponse> GetAsync(Guid vaultId, CancellationToken ct) =>
        await BuildAsync(await OwnedAsync(vaultId, ct), ct);

    public async Task<ExecutorInvitationResponse> InviteAsync(Guid vaultId, InviteExecutorRequest request, CancellationToken ct)
    {
        OpaqueToken token = tokens.CreateOpaqueToken();
        bool accountExists = false;
        string ownerName = string.Empty;
        Executor executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Vault vault = await OwnedAsync(vaultId, ct);
            await EnsureInvitationEditableAsync(vault, ct);
            string email = request.Email.Trim().ToLowerInvariant();
            User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
            ownerName = owner!.FullName;
            if (email == owner.Email.Trim().ToLowerInvariant())
            {
                throw NotAllowed("OWNER_CANNOT_ACCEPT");
            }
            User? inviteeUser = await users.FindByEmailAsync(email, ct);
            if (inviteeUser == null || inviteeUser.Role != UserRole.Executor || inviteeUser.Status != UserStatus.Active)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.OnlySystemExecutorsAllowed,
                    "Hệ thống hiện chỉ hỗ trợ chỉ định Người thực thi hệ thống đang hoạt động.");
            }
            accountExists = true;
            DateTimeOffset now = time.GetUtcNow();
            foreach (Executor previous in vault.Executors.Where(e => e.Status == ExecutorStatus.Invited && e.ExpiresAt <= now))
            {
                previous.Status = ExecutorStatus.Expired;
                previous.EndedAt = now;
                previous.UpdatedAt = now;
            }
            Guid? replaces = null;
            List<Executor> occupying = vault.Executors.Where(e => e.Status is ExecutorStatus.Invited or ExecutorStatus.Accepted or ExecutorStatus.Suspended).ToList();
            if (occupying.Count > 0)
            {
                // SETUP-06: kế hoạch đang chạy mà đã có Executor đã chấp nhận → mời người thay; người cũ giữ vai trò
                // cho tới khi người mới chấp nhận. Mọi trường hợp khác vẫn là trùng lời mời.
                bool replacement = PlanRules.IsRunning(vault.Plan.Status)
                    && occupying.Count == 1 && occupying[0].Status == ExecutorStatus.Accepted;
                if (!replacement)
                {
                    throw Conflict();
                }
                replaces = occupying[0].Id;
            }
            // Mời sau cùng: kế hoạch nháp phải xong mọi bước khác để người được mời có tờ đầy đủ mà xem trước khi ký.
            if (vault.Plan.Status == PlanStatus.Draft)
            {
                PlanSetupResponse setup = await BuildAsync(vault, ct);
                if (!setup.CanInviteExecutor)
                {
                    throw SetupNotComplete(setup);
                }
            }
            // Người thực thi là nhân sự LegacyVault, không phải người nhận.
            Executor item = new Executor
            {
                VaultId = vault.Id, Status = ExecutorStatus.Invited,
                InviteeEmail = email, InvitationTokenHash = token.Hash,
                InvitedAt = now, ExpiresAt = now + RuntimeSettings.Days("Executor.InvitationExpiresDays", 7),
                ReplacesExecutorId = replaces,
            };
            invitations.Add(item);
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        return await DeliverAsync(executor, token.Value, accountExists, ct, ownerName: ownerName);
    }

    public async Task<ExecutorInvitationResponse> AssignByAdminAsync(Guid vaultId, AssignExecutorRequest request, CancellationToken ct)
    {
        Guid adminId = currentUser.RequireUserId();
        User? admin = await users.FindByIdAsync(adminId, ct);
        if (admin == null || admin.Role != UserRole.Admin || admin.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        string? reason = AuditRules.NormalizeReason(request.Reason);
        if (reason == null)
        {
            throw ValidationErrors.For("reason", "Vui lòng ghi lý do.");
        }

        OpaqueToken token = tokens.CreateOpaqueToken();
        bool accountExists = true;
        bool assignedImmediately = false;
        Guid? formerExecutorUserId = null;
        Executor executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            assignedImmediately = false;
            formerExecutorUserId = null;
            Vault vault = await vaults.FindByIdAsync(vaultId, ct)
                ?? throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
            if (!ExecutorAssignRules.PlanAllowsAssign(vault.Status, vault.Plan.Status))
            {
                throw NotAllowed("PLAN_LOCKED");
            }
            User target = await users.FindByIdAsync(request.UserId!.Value, ct)
                ?? throw ValidationErrors.For("userId", "Không tìm thấy tài khoản được giao.");
            if (target.Status != UserStatus.Active || target.EmailVerifiedAt == null)
            {
                throw ValidationErrors.For("userId", "Chỉ giao cho tài khoản đang hoạt động và đã xác minh email.");
            }
            if (target.Id == vault.OwnerId)
            {
                throw NotAllowed("OWNER_CANNOT_ACCEPT");
            }
            if (target.Role == UserRole.Admin || target.Role == UserRole.Verifier)
            {
                throw NotAllowed("ROLE_CONFLICT");
            }
            // Cùng luật với Chủ tài sản mời: chỉ giao cho Người thực thi hệ thống đã được cấp vai trò.
            if (target.Role != UserRole.Executor)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.OnlySystemExecutorsAllowed,
                    "Hệ thống hiện chỉ hỗ trợ chỉ định Người thực thi hệ thống đang hoạt động.");
            }

            DateTimeOffset now = time.GetUtcNow();
            bool duringHandover = ExecutorAssignRules.IsHandoverUnderway(vault.Plan.Status);
            bool hadAccepted = false;
            foreach (Executor existingExecutor in vault.Executors)
            {
                if (existingExecutor.Status == ExecutorStatus.Accepted)
                {
                    hadAccepted = true;
                    if (existingExecutor.UserId == target.Id && duringHandover)
                    {
                        throw NotAllowed("ALREADY_EXECUTOR");
                    }
                    if (existingExecutor.UserId != null)
                    {
                        formerExecutorUserId = existingExecutor.UserId;
                    }
                }
            }
            Guid? replaces = ExecutorAssignRules.PrepareForAssign(vault.Executors, vault.Plan.Status, now);
            if (hadAccepted && replaces == null && !duringHandover)
            {
                // Người đã chấp nhận bị thu hồi ngay (kế hoạch chưa chạy): đổi mã nếu kho đã có mã, để người cũ không còn mã đúng.
                // Đang bàn giao thì KHÔNG đổi mã: mã có thể đã gửi cho người nhận; người cũ mất quyền đọc mã trên hệ thống ngay.
                await keyIssuer.RotateIfExistsAsync(vault, AuditActorRole.System, null, now, ct);
            }
            Executor item = new Executor
            {
                VaultId = vault.Id, Status = ExecutorStatus.Invited,
                InviteeEmail = target.Email.Trim().ToLowerInvariant(), InvitationTokenHash = token.Hash,
                InvitedAt = now, ExpiresAt = now.AddDays(RuntimeSettings.Int("Executor.InvitationExpiresDays", 7)),
                ReplacesExecutorId = replaces,
            };
            if (duringHandover)
            {
                // Đang bàn giao: người mới nhận việc ngay (không chờ chấp nhận) để ký, gửi mã nhận, ký phiếu giao nhận.
                // Chữ ký Người thực thi người cũ đã ký trên bản hiện tại vẫn giữ hiệu lực.
                item.Status = ExecutorStatus.Accepted;
                item.UserId = target.Id;
                item.AcceptedAt = now;
                item.AcceptedScopeVersion = ScopeVersion;
                item.UpdatedAt = now;
                assignedImmediately = true;
            }
            invitations.Add(item);
            PlanRules.BumpConfigVersion(vault.Plan, now);
            audit.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.ExecutorAssignedByAdmin,
                AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, reason, now));
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        if (assignedImmediately)
        {
            // Đang bàn giao: không có lời mời; báo người mới, người cũ và luật sư của biên bản sau khi lưu.
            await NotifyExecutorChangedAsync(executor, formerExecutorUserId, ct);
            return Map(executor);
        }
        // Thư do quản trị viên giao, lời thư không nói "Chủ tài sản mời".
        return await DeliverAsync(executor, token.Value, accountExists, ct, assignedByAdmin: true);
    }

    /// <summary>
    /// Báo đổi Người thực thi khi đang bàn giao: người mới (nhận việc ngay), người cũ (thôi vai trò) và luật sư của biên bản
    /// đang mở. Thư lỗi thì bỏ qua: việc đổi người đã lưu.
    /// </summary>
    private async Task NotifyExecutorChangedAsync(Executor executor, Guid? formerExecutorUserId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(executor.VaultId, ct);
        if (vault == null)
        {
            return;
        }
        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        string ownerName = string.Empty;
        if (owner != null)
        {
            ownerName = owner.FullName;
        }
        List<(Guid UserId, string Audience)> targets = new List<(Guid UserId, string Audience)>();
        if (executor.UserId != null)
        {
            targets.Add((executor.UserId.Value, HandoverRequestNotice.Executor));
        }
        if (formerExecutorUserId != null)
        {
            targets.Add((formerExecutorUserId.Value, HandoverRequestNotice.FormerExecutor));
        }
        HandoverRecord? record = await handoverRecords.FindByVaultIdAsync(vault.Id, ct);
        if (record != null)
        {
            targets.Add((record.VerifierUserId, HandoverRequestNotice.Verifier));
        }
        foreach ((Guid userId, string audience) in targets)
        {
            try
            {
                User? target = await users.FindByIdAsync(userId, ct);
                if (target == null || string.IsNullOrWhiteSpace(target.Email))
                {
                    continue;
                }
                HandoverRequestNotice notice = new HandoverRequestNotice();
                notice.ToEmail = target.Email;
                notice.FullName = target.FullName;
                notice.Audience = audience;
                notice.Stage = HandoverRequestNotice.StageExecutorChanged;
                notice.OwnerName = ownerName;
                await handoverNotices.SendAsync(notice, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Không ném lại: Người thực thi đã đổi; một thư lỗi không chặn các thư còn lại.
            }
        }
    }

    public async Task<ExecutorInvitationResponse> ResendAsync(Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct)
    {
        OpaqueToken token = tokens.CreateOpaqueToken();
        bool accountExists = false;
        string ownerName = string.Empty;
        Executor executor = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Vault vault = await OwnedAsync(vaultId, ct);
            await EnsureInvitationEditableAsync(vault, ct);
            ownerName = (await users.FindByIdAsync(vault.OwnerId, ct))!.FullName;
            Executor item = vault.Executors.SingleOrDefault(e => e.Id == executorId) ?? throw InvalidInvitation();
            if (!Etag.Matches(request.Etag, item.UpdatedAt))
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict, "Lời mời đã thay đổi. Tải lại trước khi gửi tiếp.");
            }
            if (item.Status is not (ExecutorStatus.Invited or ExecutorStatus.Expired or ExecutorStatus.Suspended)
                && !(item.Status == ExecutorStatus.Accepted && item.AcceptedScopeVersion != ScopeVersion))
            {
                throw NotAllowed("WRONG_STATE");
            }
            User? inviteeUser = await users.FindByEmailAsync(item.InviteeEmail, ct);
            accountExists = inviteeUser is not null;
            DateTimeOffset now = time.GetUtcNow();
            item.Status = ExecutorStatus.Invited;
            item.InvitationTokenHash = token.Hash;
            item.InvitedAt = now;
            item.ExpiresAt = now + RuntimeSettings.Days("Executor.InvitationExpiresDays", 7);
            item.InvitationSentAt = null;
            item.AcceptedAt = null;
            item.AcceptedScopeVersion = null;
            item.ConfirmedWillVersionNo = null;
            item.WillConfirmedAt = null;
            item.DeclinedWillVersionNo = null;
            item.EndedAt = null;
            item.UpdatedAt = now;
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return item;
        }, ct);
        return await DeliverAsync(executor, token.Value, accountExists, ct, ownerName: ownerName);
    }

    public async Task<ExecutorInvitationInfo> InspectAsync(string token, CancellationToken ct)
    {
        Executor item = await FindTokenAsync(token, ct);
        if (item.Status != ExecutorStatus.Accepted && (item.Status != ExecutorStatus.Invited || item.ExpiresAt <= time.GetUtcNow()))
        {
            throw InvalidInvitation();
        }
        return new(item.ExpiresAt, item.Status.ToApiString(), ScopeVersion, ScopeText);
    }

    public Task<ExecutorInvitationResponse> RespondAsync(RespondExecutorInvitationRequest request, CancellationToken ct)
    {
        return RespondCoreAsync(request.Accept, request.AcknowledgeScope, request.ScopeVersion, request.Password,
            request.WillVersionNo, responder => FindTokenAsync(request.Token, ct), ct);
    }

    public Task<ExecutorInvitationResponse> RespondByIdAsync(
        Guid invitationId, RespondMyExecutorInvitationRequest request, CancellationToken ct)
    {
        return RespondCoreAsync(request.Accept, request.AcknowledgeScope, request.ScopeVersion, request.Password,
            request.WillVersionNo, responder => FindMyInvitationAsync(invitationId, responder, ct), ct);
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
            // Chủ tài sản không nhận lời mời của chính kho mình; lời mời đã nhận nằm ở danh sách kho của Executor.
            if (item.Vault.OwnerId == userId)
            {
                continue;
            }

            bool expired = item.Status == ExecutorStatus.Invited && item.ExpiresAt <= now;
            if (item.Status == ExecutorStatus.Invited && !expired)
            {
                MyExecutorInvitationResponse pending = ToMyInvitation(item, "INVITED", null);
                await FillWillAsync(pending, item, ct);
                response.Pending.Add(pending);
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

    /// <summary>
    /// Người thực thi "ký" nhận vai bằng mật khẩu tài khoản (chỉ khi chấp nhận; từ chối không cần mật khẩu).
    /// Kiểm mật khẩu làm TRƯỚC khối serializable: ISigningPasswordChecker lưu bộ đếm sai rồi mới ném lỗi, nếu nằm trong
    /// giao dịch serializable thì lỗi ném ra sẽ hoàn tác luôn bộ đếm. Chỉ kiểm khi lời mời còn hợp lệ cho người này,
    /// để link sai/hết hạn không làm tốn lượt thử; các luật còn lại vẫn được kiểm lại đầy đủ trong giao dịch.
    /// </summary>
    private async Task<ExecutorInvitationResponse> RespondCoreAsync(bool accept, bool acknowledgeScope, string scopeVersion,
        string? password, int? willVersionNo, Func<User, Task<Executor>> findInvitation, CancellationToken ct)
    {
        if (accept)
        {
            Guid currentUserId = currentUser.RequireUserId();
            User signer = await UsableUserAsync(currentUserId, ct);
            if (scopeVersion != ScopeVersion || !acknowledgeScope)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.PolicyAcknowledgementRequired, "Cần đọc và xác nhận phạm vi nhiệm vụ hiện hành.");
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                throw ValidationErrors.For("password", "Nhập mật khẩu tài khoản để ký nhận vai Người thực thi.");
            }

            Executor pending = await findInvitation(signer);
            DateTimeOffset checkTime = time.GetUtcNow();
            bool emailMatches = string.Equals(signer.Email?.Trim(), pending.InviteeEmail?.Trim(), StringComparison.OrdinalIgnoreCase);
            bool belongsToSigner = !pending.UserId.HasValue || pending.UserId == currentUserId;
            bool stillOpen = pending.Status == ExecutorStatus.Invited && pending.ExpiresAt > checkTime;
            if (stillOpen && emailMatches && belongsToSigner)
            {
                await signingPasswordChecker.CheckAsync(signer, password, checkTime, ct);
            }
        }
        Guid? activatedVaultId = null;
        ExecutorInvitationResponse response = await RespondInTransactionAsync(accept, acknowledgeScope, scopeVersion, willVersionNo,
            vaultId => activatedVaultId = vaultId, findInvitation, ct);
        if (activatedVaultId != null)
        {
            // Báo người nhận sau khi đã lưu kích hoạt, ngoài giao dịch để lần thử lại không gửi trùng thư.
            await activatedNotifier.NotifyAsync(activatedVaultId.Value, ct);
        }
        return response;
    }

    private Task<ExecutorInvitationResponse> RespondInTransactionAsync(bool accept, bool acknowledgeScope, string scopeVersion,
        int? willVersionNo, Action<Guid> markActivated, Func<User, Task<Executor>> findInvitation, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Guid userId = currentUser.RequireUserId();
            User responder = await UsableUserAsync(userId, ct);
            if (scopeVersion != ScopeVersion || (accept && !acknowledgeScope))
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.PolicyAcknowledgementRequired, "Cần đọc và xác nhận phạm vi nhiệm vụ hiện hành.");
            }
            Executor item = await findInvitation(responder);
            Vault vault = await vaults.FindByIdAsync(item.VaultId, ct) ?? throw InvalidInvitation();
            if (vault.OwnerId == userId)
            {
                throw NotAllowed("OWNER_CANNOT_ACCEPT");
            }
            if (item.UserId.HasValue && item.UserId != userId)
            {
                throw Conflict();
            }
            if (item.UserId == userId && ((accept && item.Status == ExecutorStatus.Accepted) || (!accept && item.Status == ExecutorStatus.Declined)))
            {
                return Map(item);
            }
            if (item.Status != ExecutorStatus.Invited || item.ExpiresAt <= time.GetUtcNow())
            {
                throw InvalidInvitation();
            }
            // M4, SETUP-20: người trả lời phải dùng tài khoản có email đã xác minh trùng email được mời; link bị chuyển tiếp
            // hay tài khoản email khác không nhận được vai trò Executor.
            if (!string.Equals(responder.Email?.Trim(), item.InviteeEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw NotAllowed("INVITEE_EMAIL_MISMATCH");
            }
            VaultRules.EnsureEditable(vault);
            if (vault.Plan.Status != PlanStatus.Draft && !PlanRules.IsRunning(vault.Plan.Status))
            {
                throw NotAllowed("PLAN_NOT_DRAFT");
            }
            if (accept && vault.Executors.Any(e => e.Id != item.Id && e.UserId == userId && e.Status == ExecutorStatus.Accepted))
            {
                throw NotAllowed("INDEPENDENT_EXECUTOR_REQUIRED");
            }
            DateTimeOffset now = time.GetUtcNow();
            if (accept)
            {
                // Người thực thi ký sau cùng: phải có tờ Chủ tài sản đã ký và đúng phiên bản người này vừa xem.
                WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
                List<AssetAssignment> current = await assignments.FindByVaultIdAsync(vault.Id, ct);
                if (latest == null || !WillVersionRules.IsReadyForInvitee(vault.Plan, item, latest, current))
                {
                    throw NotAllowed("WILL_NOT_SIGNED");
                }
                if (willVersionNo != latest.VersionNo)
                {
                    throw NotAllowed("WILL_VERSION_CHANGED");
                }
                if (vault.Plan.Status == PlanStatus.Draft)
                {
                    PlanSetupResponse setup = await BuildAsync(vault, ct);
                    if (!setup.CanInviteExecutor)
                    {
                        throw SetupNotComplete(setup);
                    }
                }
                item.ConfirmedWillVersionNo = latest.VersionNo;
                item.WillConfirmedAt = now;
                item.DeclinedWillVersionNo = null;
            }
            item.UserId = userId;
            item.Status = accept ? ExecutorStatus.Accepted : ExecutorStatus.Declined;
            item.AcceptedAt = accept ? now : null;
            item.AcceptedScopeVersion = accept ? ScopeVersion : null;
            item.EndedAt = accept ? null : now;
            item.UpdatedAt = now;
            if (accept && item.ReplacesExecutorId is Guid replacedId)
            {
                // SETUP-06: người thay chấp nhận thì người cũ thôi vai trò trong cùng giao dịch.
                Executor? replaced = vault.Executors.SingleOrDefault(e => e.Id == replacedId && e.Status == ExecutorStatus.Accepted);
                if (replaced is not null)
                {
                    replaced.Status = ExecutorStatus.Revoked;
                    replaced.EndedAt = now;
                    replaced.UpdatedAt = now;
                    // Đổi Người thực thi: kho đã có mã thì tự đổi mã (version + 1) để người cũ không còn mã đúng.
                    await keyIssuer.RotateIfExistsAsync(vault, AuditActorRole.System, null, now, ct);
                }
            }
            if (accept)
            {
                // Người thực thi tự chấp nhận là bước ký xác nhận di chúc; chỉ ghi phiên bản/đích, không ghi nội dung.
                audit.Add(AuditRules.Create(vault.Id, userId, AuditActorRole.Executor, AuditAction.ExecutorAccepted,
                    AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, null, now));
            }
            if (accept && vault.Plan.Status == PlanStatus.Draft)
            {
                // Chữ ký cuối cùng: kế hoạch có hiệu lực từ lúc này.
                await ActivatePlanAsync(vault, userId, AuditActorRole.Executor, now, ct);
                markActivated(vault.Id);
            }
            else
            {
                PlanRules.BumpConfigVersion(vault.Plan, now);
            }
            await unitOfWork.SaveChangesAsync(ct);
            return Map(item);
        }, ct);

    public async Task<PlanSetupResponse> ActivateAsync(Guid vaultId, ActivatePlanRequest request, CancellationToken ct)
    {
        bool activated = false;
        PlanSetupResponse response = await ActivateCoreAsync(vaultId, request, () => activated = true, ct);
        if (activated)
        {
            // Báo người nhận sau khi đã lưu kích hoạt, ngoài giao dịch để lần thử lại không gửi trùng thư.
            await activatedNotifier.NotifyAsync(vaultId, ct);
        }
        return response;
    }

    private Task<PlanSetupResponse> ActivateCoreAsync(Guid vaultId, ActivatePlanRequest request, Action markActivated, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Vault vault = await OwnedAsync(vaultId, ct);
            await UsableUserAsync(vault.OwnerId, ct);
            if (!request.AcknowledgePolicy || request.PolicyVersion != PolicyVersion)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.PolicyAcknowledgementRequired, "Cần đọc và xác nhận chính sách hiện hành.");
            }
            if (request.ConfigVersion != vault.Plan.ConfigVersion)
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict, "Cấu hình kế hoạch đã thay đổi. Tải lại trước khi kích hoạt.");
            }
            // A duplicate request returns the existing state without resetting deadlines or opening a period.
            if (vault.Plan.Status == PlanStatus.Active)
            {
                return await BuildAsync(vault, ct);
            }
            if (vault.Plan.Status != PlanStatus.Draft)
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.PlanActivationNotAllowed, "Trạng thái kế hoạch không cho phép kích hoạt lại.");
            }
            PlanSetupResponse setup = await BuildAsync(vault, ct);
            if (!setup.CanActivate)
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.PlanActivationRequirementsNotMet,
                    "Kế hoạch chưa đủ điều kiện kích hoạt.", new Dictionary<string, object?> { ["missingRequirements"] = setup.MissingRequirements });
            }
            if (setup.WillAwaitingExecutor)
            {
                // Bấm lặp: đã ký đúng bản này, chỉ còn chờ Người thực thi.
                return setup;
            }
            DateTimeOffset now = time.GetUtcNow();
            vault.Plan.ActivationPolicyVersion = PolicyVersion;
            vault.Plan.ActivationPolicyAcknowledgedAt = now;
            vault.Plan.UpdatedAt = now;
            // Ký bằng PIN cũng là lúc chụp lại cấu hình: bàn giao chỉ theo bản ký này cho tới khi Chủ tài sản ký lại.
            List<AssetAssignment> signedAssignments = await assignments.FindByVaultIdAsync(vault.Id, ct);
            WillVersion version = await AddWillVersionAsync(vault, signedAssignments, now, ct);
            Executor? invitee = FindPendingInvitee(vault, now);
            bool activated = invitee == null;
            if (invitee != null)
            {
                // Chủ tài sản ký trước; kế hoạch chỉ có hiệu lực khi Người thực thi xem và ký.
                version.ExecutorId = invitee.Id;
                audit.Add(AuditRules.Create(vault.Id, vault.OwnerId, AuditActorRole.Owner, AuditAction.WillSigned,
                    AuditTargetType.Vault, vault.Id, version.VersionNo, AuditOutcome.Succeeded, null, now));
            }
            else
            {
                // Dữ liệu cũ: Người thực thi đã nhận vai từ khi kế hoạch còn nháp, sẽ được mời đồng ý tờ này sau.
                await ActivatePlanAsync(vault, vault.OwnerId, AuditActorRole.Owner, now, ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            if (activated)
            {
                markActivated();
            }
            return await BuildAsync(vault, ct);
        }, ct);

    /// <summary>Đặt kế hoạch đang chạy và tạo mã nhận (không trả mã gốc). Người gọi lưu cùng thao tác chính.</summary>
    private async Task ActivatePlanAsync(Vault vault, Guid actorId, AuditActorRole actorRole, DateTimeOffset now, CancellationToken ct)
    {
        vault.Plan.Status = PlanStatus.Active;
        vault.Plan.ActivatedAt = now;
        vault.Plan.UpdatedAt = now;
        await keyIssuer.EnsureReadableAsync(vault, actorRole, actorId, now, ct);
        audit.Add(AuditRules.Create(vault.Id, actorId, actorRole, AuditAction.PlanActivated,
            AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, null, now));
    }

    /// <summary>Lời mời còn hạn, chưa trả lời; không có thì null.</summary>
    private static Executor? FindPendingInvitee(Vault vault, DateTimeOffset now)
    {
        foreach (Executor item in vault.Executors)
        {
            if (item.Status == ExecutorStatus.Invited && item.ExpiresAt > now)
            {
                return item;
            }
        }
        return null;
    }

    private static AppException SetupNotComplete(PlanSetupResponse setup)
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed,
            "Chủ tài sản cần hoàn tất các bước khác trước khi mời Người thực thi.",
            new Dictionary<string, object?>
            {
                ["reason"] = "SETUP_NOT_COMPLETE",
                ["missingRequirements"] = setup.MissingRequirements,
            });
    }

    public async Task<PlanSetupResponse> SignWillAsync(Guid vaultId, SignWillRequest request, CancellationToken ct)
    {
        int? createdVersionNo = null;
        PlanSetupResponse response = await SignWillCoreAsync(vaultId, request, versionNo => createdVersionNo = versionNo, ct);
        if (createdVersionNo != null)
        {
            await NotifyNewWillVersionAsync(vaultId, createdVersionNo.Value, ct);
        }
        return response;
    }

    /// <summary>Báo Người thực thi có phiên bản mới cần xem; thư lỗi thì bỏ qua vì chữ ký đã lưu.</summary>
    private async Task NotifyNewWillVersionAsync(Guid vaultId, int versionNo, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null)
        {
            return;
        }
        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        foreach (Executor item in vault.Executors)
        {
            if (item.Status != ExecutorStatus.Accepted || item.UserId == null)
            {
                continue;
            }
            User? target = await users.FindByIdAsync(item.UserId.Value, ct);
            if (target != null && owner != null)
            {
                await willNotices.SendNewVersionAsync(target.Email, owner.FullName, versionNo, ct);
            }
        }
    }

    private Task<PlanSetupResponse> SignWillCoreAsync(
        Guid vaultId, SignWillRequest request, Action<int> markCreated, CancellationToken ct)
    {
        return unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Vault vault = await OwnedAsync(vaultId, ct);
            await UsableUserAsync(vault.OwnerId, ct);
            if (!PlanRules.IsRunning(vault.Plan.Status))
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.WillSignNotAllowed,
                    "Chỉ ký lại biên bản phân chia được khi kế hoạch đang chạy.");
            }
            if (request.ConfigVersion != vault.Plan.ConfigVersion)
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                    "Cấu hình kế hoạch đã thay đổi. Tải lại trước khi ký.");
            }

            List<AssetAssignment> currentAssignments = await assignments.FindByVaultIdAsync(vault.Id, ct);
            WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
            if (latest != null)
            {
                WillSnapshot signed = WillVersionRules.FromJson(latest.SnapshotJson);
                WillSnapshot current = WillVersionRules.BuildSnapshot(currentAssignments);
                UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, current);
                if (!WillVersionRules.HasChanges(changes))
                {
                    // Gọi lặp hoặc không có gì mới để ký: trả nguyên trạng, không tạo phiên bản.
                    return await BuildAsync(vault, ct);
                }
            }

            DateTimeOffset now = time.GetUtcNow();
            WillVersion created = await AddWillVersionAsync(vault, currentAssignments, now, ct);
            audit.Add(AuditRules.Create(vault.Id, vault.OwnerId, AuditActorRole.Owner, AuditAction.WillResigned,
                AuditTargetType.Vault, vault.Id, created.VersionNo, AuditOutcome.Succeeded, null, now));
            await unitOfWork.SaveChangesAsync(ct);
            markCreated(created.VersionNo);
            return await BuildAsync(vault, ct);
        }, ct);
    }

    /// <summary>Tạo phiên bản di chúc kế tiếp từ cấu hình hiện tại (chưa lưu; người gọi SaveChanges cùng thao tác chính).</summary>
    private async Task<WillVersion> AddWillVersionAsync(
        Vault vault, List<AssetAssignment> currentAssignments, DateTimeOffset now, CancellationToken ct)
    {
        WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        int nextNo = 1;
        if (latest != null)
        {
            nextNo = latest.VersionNo + 1;
        }
        WillSnapshot snapshot = WillVersionRules.BuildSnapshot(currentAssignments);
        WillVersion version = WillVersionRules.CreateVersion(vault.Id, nextNo, vault.Plan.ConfigVersion, snapshot, now);
        willVersions.Add(version);
        return version;
    }

    private async Task<Vault> OwnedAsync(Guid id, CancellationToken ct) =>
        VaultRules.EnsureOwnedBy(await vaults.FindByIdAsync(id, ct), currentUser.RequireUserId());

    private async Task<User> UsableUserAsync(Guid id, CancellationToken ct)
    {
        User user = await users.FindByIdAsync(id, ct) ?? throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired, "Phiên đã hết hạn.");
        if (user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản hiện không được phép thực hiện thao tác.");
        }
        if (user.EmailVerifiedAt is null)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.EmailNotVerified, "Cần xác minh email trước khi tiếp tục.");
        }
        return user;
    }

    private async Task EnsureInvitationEditableAsync(Vault vault, CancellationToken ct)
    {
        await UsableUserAsync(vault.OwnerId, ct);
        VaultRules.EnsureEditable(vault);
        if (vault.Plan.Status != PlanStatus.Draft && !PlanRules.IsRunning(vault.Plan.Status))
        {
            throw NotAllowed("PLAN_NOT_DRAFT");
        }
        if (!Paid(vault))
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.PaidTierRequired, "Cần gói trả phí còn hiệu lực.");
        }
    }

    private bool Paid(Vault vault) => vault.Entitlement.Tier != ServiceTier.Free
        && vault.Entitlement.Status == EntitlementStatus.PaidActive
        && vault.Entitlement.ServiceExpiresAt > time.GetUtcNow();

    private async Task<PlanSetupResponse> BuildAsync(Vault vault, CancellationToken ct)
    {
        List<string> missing = new List<string>();
        User? user = await users.FindByIdAsync(vault.OwnerId, ct);
        if (user?.Status != UserStatus.Active)
        {
            missing.Add("ACCOUNT_NOT_ACTIVE");
        }
        if (user?.EmailVerifiedAt is null)
        {
            missing.Add("EMAIL_VERIFICATION_REQUIRED");
        }
        if (vault.Status != VaultStatus.Open)
        {
            missing.Add("VAULT_NOT_OPEN");
        }
        if (!Paid(vault))
        {
            missing.Add("PAID_SERVICE_REQUIRED");
        }
        // EKYC-01: chỉ lượt eKYC đăng ký Owner. Đã từng đạt thì giữ, chụp lại giữa chừng không làm mất kết quả.
        EkycVerification? latestPassedEkyc = await ekyc.FindLatestPassedByUserAndPurposeAsync(vault.OwnerId, EkycPurpose.OwnerEnrollment, ct);
        if (latestPassedEkyc == null)
        {
            missing.Add("OWNER_EKYC_REQUIRED");
        }
        int assignmentCount = await assignments.CountByVaultIdAsync(vault.Id, ct);
        List<Asset> unassignedAssets = await assets.FindUnassignedByVaultIdAsync(vault.Id, ct);
        // Không bắt buộc khai người nhận trước (luật sư xác định khi bàn giao); chỉ cần kho có tài sản.
        if (assignmentCount == 0 && unassignedAssets.Count == 0)
        {
            missing.Add("ASSET_REQUIRED");
        }
        List<Executor> accepted = new List<Executor>();
        foreach (Executor item in vault.Executors)
        {
            if (!item.UserId.HasValue)
            {
                continue;
            }

            bool isAcceptedScopeValid = item.Status == ExecutorStatus.Accepted
                && item.AcceptedAt.HasValue
                && item.AcceptedScopeVersion == ScopeVersion;

            if (!isAcceptedScopeValid)
            {
                continue;
            }

            User? executorUser = await users.FindByIdAsync(item.UserId.Value, ct);
            bool isExecutorEligible = executorUser?.Status == UserStatus.Active
                && executorUser.EmailVerifiedAt.HasValue
                && executorUser.Id != vault.OwnerId;

            if (isExecutorEligible)
            {
                accepted.Add(item);
            }
        }
        // Mỗi kho đúng một Executor đã chấp nhận (không còn Executor dự phòng).
        // Mời Người thực thi là bước sau cùng: chỉ mở khi mọi bước khác đã xong.
        bool canInviteExecutor = missing.Count == 0;
        Executor? pendingInvitee = null;
        if (vault.Plan.Status == PlanStatus.Draft)
        {
            pendingInvitee = FindPendingInvitee(vault, time.GetUtcNow());
        }
        // Chủ tài sản ký được khi đã mời một người (chưa trả lời) hoặc có người đã nhận vai (dữ liệu cũ).
        if (accepted.Count != 1 && pendingInvitee == null)
        {
            missing.Add("PRIMARY_EXECUTOR_REQUIRED");
        }
        if (vault.Plan.Status != PlanStatus.Draft)
        {
            missing.Add("PLAN_NOT_DRAFT");
        }
        List<AssetAssignment> vaultAssignments = await assignments.FindDetailedByVaultIdAsync(vault.Id, ct);
        List<PlanSetupRecipientResponse> recipients = await BuildRecipientsAsync(vault, vaultAssignments, ct);
        List<ExecutorInvitationResponse> executorItems = new List<ExecutorInvitationResponse>();
        foreach (Executor executorItem in vault.Executors.OrderByDescending(e => e.InvitedAt))
        {
            string? executorFullName = null;
            if (executorItem.UserId.HasValue)
            {
                User? linkedUser = await users.FindByIdAsync(executorItem.UserId.Value, ct);
                if (linkedUser != null)
                {
                    executorFullName = linkedUser.FullName;
                }
            }
            executorItems.Add(Map(executorItem, false, executorFullName));
        }
        HashSet<string> bouncedEmails = await bounces.FindBouncedAsync(executorItems.Select(e => e.InviteeEmail), ct);
        for (int i = 0; i < executorItems.Count; i++)
        {
            if (bouncedEmails.Contains(executorItems[i].InviteeEmail.Trim().ToLowerInvariant()))
            {
                executorItems[i] = executorItems[i] with { EmailUndeliverable = true };
            }
        }
        string ownerFullName = string.Empty;
        string ownerEmail = string.Empty;
        if (user != null)
        {
            ownerFullName = user.FullName;
            ownerEmail = user.Email;
        }
        int? willVersionNo = null;
        DateTimeOffset? willSignedAt = null;
        bool hasUnsignedChanges = false;
        UnsignedWillChangesResponse unsignedChanges = WillVersionRules.NoChanges();
        WillVersion? latestWill = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        if (latestWill != null)
        {
            willVersionNo = latestWill.VersionNo;
            willSignedAt = latestWill.SignedAt;
            // Chỉ kế hoạch đang chạy mới có "thay đổi chưa ký"; kế hoạch nháp sẽ ký lại khi kích hoạt.
            if (PlanRules.IsRunning(vault.Plan.Status))
            {
                WillSnapshot signedSnapshot = WillVersionRules.FromJson(latestWill.SnapshotJson);
                WillSnapshot currentSnapshot = WillVersionRules.BuildSnapshot(vaultAssignments);
                unsignedChanges = WillVersionRules.Compare(signedSnapshot, currentSnapshot);
                hasUnsignedChanges = WillVersionRules.HasChanges(unsignedChanges);
            }
        }
        bool willAwaitingExecutor = pendingInvitee != null
            && WillVersionRules.IsReadyForInvitee(vault.Plan, pendingInvitee, latestWill, vaultAssignments);
        // Cảnh báo duy nhất trước đây là NO_BACKUP_EXECUTOR; đã bỏ Executor dự phòng nên giữ mảng rỗng cho đúng hợp đồng API.
        return new(vault.Plan.Status.ToApiString(), vault.Plan.ConfigVersion, missing.Count == 0,
            missing.ToArray(), PolicyVersion, PolicyText, ScopeVersion, ScopeText,
            executorItems,
            CountDistinctAssets(vaultAssignments), PlanSetupMappings.ToAssetItems(unassignedAssets), Array.Empty<string>(),
            ownerFullName, ownerEmail, vault.Plan.ActivatedAt, recipients,
            willVersionNo, willSignedAt, hasUnsignedChanges, unsignedChanges, willAwaitingExecutor, canInviteExecutor);
    }

    /// <summary>
    /// Người nhận theo từng người, lấy từ các dòng gán của kho. Tên được giải mã từ dữ liệu cá nhân đã mã hóa;
    /// chỉ trả hasMessage chứ không trả nội dung lời nhắn, và không ghi log dữ liệu cá nhân.
    /// Mỗi người kèm các tài sản được gán, và số người cùng nhận chung từng tài sản.
    /// </summary>
    private async Task<List<PlanSetupRecipientResponse>> BuildRecipientsAsync(
        Vault vault, List<AssetAssignment> vaultAssignments, CancellationToken ct)
    {
        // Số người nhận của từng tài sản, để biết tài sản nào đang nhận chung.
        Dictionary<Guid, int> recipientCountByAsset = new Dictionary<Guid, int>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            if (recipientCountByAsset.ContainsKey(assignment.AssetId))
            {
                recipientCountByAsset[assignment.AssetId] = recipientCountByAsset[assignment.AssetId] + 1;
            }
            else
            {
                recipientCountByAsset[assignment.AssetId] = 1;
            }
        }

        Dictionary<Guid, PlanSetupRecipientResponse> byBeneficiary = new Dictionary<Guid, PlanSetupRecipientResponse>();
        HashSet<Guid> skipped = new HashSet<Guid>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            Guid beneficiaryId = assignment.BeneficiaryId;
            if (skipped.Contains(beneficiaryId))
            {
                continue;
            }
            if (!byBeneficiary.ContainsKey(beneficiaryId))
            {
                Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, beneficiaryId, ct);
                if (beneficiary == null)
                {
                    skipped.Add(beneficiaryId);
                    continue;
                }
                string fullName = string.Empty;
                bool hasMessage = false;
                try
                {
                    BeneficiaryPersonalData data = beneficiaryProtector.Unprotect(beneficiary);
                    fullName = data.FullName;
                    hasMessage = !string.IsNullOrWhiteSpace(data.Message);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // Không giải mã được thì vẫn liệt kê người nhận (tên rỗng), không làm hỏng cả màn kích hoạt.
                }
                byBeneficiary[beneficiaryId] = new PlanSetupRecipientResponse(
                    beneficiaryId, fullName, hasMessage, new List<PlanSetupRecipientAssetResponse>());
            }
            byBeneficiary[beneficiaryId].Assets.Add(new PlanSetupRecipientAssetResponse(
                assignment.Asset.Id, assignment.Asset.Name, assignment.Asset.Kind.ToApiString(),
                recipientCountByAsset[assignment.AssetId]));
        }
        List<PlanSetupRecipientResponse> result = new List<PlanSetupRecipientResponse>(byBeneficiary.Values);
        result.Sort(CompareRecipientsByName);
        return result;
    }

    /// <summary>Số tài sản khác nhau trong các dòng gán (tài sản nhận chung chỉ đếm một lần).</summary>
    private static int CountDistinctAssets(List<AssetAssignment> vaultAssignments)
    {
        HashSet<Guid> assetIds = new HashSet<Guid>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            assetIds.Add(assignment.AssetId);
        }
        return assetIds.Count;
    }

    private static int CompareRecipientsByName(PlanSetupRecipientResponse left, PlanSetupRecipientResponse right)
    {
        return string.Compare(left.FullName, right.FullName, StringComparison.CurrentCultureIgnoreCase);
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

    /// <summary>Tờ để người được mời xem trước: chỉ có số phiên bản, lúc ký và số đếm, không có tên tài sản hay người nhận.</summary>
    private async Task FillWillAsync(MyExecutorInvitationResponse response, Executor item, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(item.VaultId, ct);
        if (vault == null)
        {
            return;
        }
        WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        List<AssetAssignment> current = await assignments.FindByVaultIdAsync(vault.Id, ct);
        if (latest == null || !WillVersionRules.IsReadyForInvitee(vault.Plan, item, latest, current))
        {
            return;
        }
        List<Asset> unassigned = await assets.FindUnassignedByVaultIdAsync(vault.Id, ct);
        response.WillReady = true;
        response.WillVersionNo = latest.VersionNo;
        response.WillSignedAt = latest.SignedAt;
        response.AssetCount = WillVersionRules.CountAssets(current, unassigned.Count);
        response.RecipientCount = WillVersionRules.CountRecipients(WillVersionRules.FromJson(latest.SnapshotJson));
    }

    private static int CompareByExpiresAt(MyExecutorInvitationResponse left, MyExecutorInvitationResponse right)
    {
        return left.ExpiresAt.CompareTo(right.ExpiresAt);
    }

    private async Task<Executor> FindTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 512)
        {
            throw InvalidInvitation();
        }
        return await invitations.FindByTokenHashAsync(tokens.HashOpaqueToken(token), ct) ?? throw InvalidInvitation();
    }

    private async Task<ExecutorInvitationResponse> DeliverAsync(
        Executor executor, string token, bool accountExists, CancellationToken ct,
        bool assignedByAdmin = false, string? ownerName = null)
    {
        // External delivery follows the committed invitation. Failed delivery never grants access.
        bool delivered;
        if (assignedByAdmin)
        {
            delivered = await sender.SendAssignedByAdminAsync(executor.InviteeEmail, token, executor.ExpiresAt, ct);
        }
        else if (!string.IsNullOrWhiteSpace(ownerName))
        {
            delivered = await sender.SendFromOwnerAsync(executor.InviteeEmail, ownerName, token, executor.ExpiresAt, ct);
        }
        else
        {
            delivered = await sender.SendAsync(executor.InviteeEmail, token, executor.ExpiresAt, ct);
        }
        if (delivered)
        {
            executor.InvitationSentAt = time.GetUtcNow();
            executor.UpdatedAt = time.GetUtcNow();
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Map(executor, accountExists);
    }
    private ExecutorInvitationResponse Map(Executor item) => Map(item, false);
    private ExecutorInvitationResponse Map(Executor item, bool accountExists) => Map(item, accountExists, null);
    private ExecutorInvitationResponse Map(Executor item, bool accountExists, string? fullName) => new(item.Id,
        item.Status == ExecutorStatus.Invited && item.ExpiresAt <= time.GetUtcNow() ? "EXPIRED" : item.Status.ToApiString(), item.InviteeEmail,
        item.ExpiresAt, item.AcceptedAt, item.InvitationSentAt, item.AcceptedScopeVersion, Etag.From(item.UpdatedAt), accountExists,
        fullName, false, item.ConfirmedWillVersionNo, item.DeclinedWillVersionNo);
    private static AppException InvalidInvitation() => new(AppErrorType.NotFound, ErrorCode.ExecutorInvitationInvalid, "Lời mời Executor không hợp lệ hoặc đã hết hạn.");
    private static AppException Conflict() => new(AppErrorType.Conflict, ErrorCode.ExecutorInvitationConflict, "Lời mời hoặc phân công Executor đã tồn tại/thay đổi. Tải lại để kiểm tra.");
    private static AppException NotAllowed(string reason) => new(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed, "Không thể thực hiện thao tác Executor lúc này.", new Dictionary<string, object?> { ["reason"] = reason });
}
