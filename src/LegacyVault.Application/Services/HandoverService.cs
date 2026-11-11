using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class HandoverService : IHandoverService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly ITokenService tokens;
    private readonly IHandoverPolicy policy;
    private readonly IHoldReader holdReader;
    private readonly IExecutorKeyShardRepository? keyShards;
    private readonly IHandoverSubmissionRepository? submissions;
    private readonly IBeneficiaryRepository? beneficiaries;
    private readonly IBeneficiaryDataProtector? beneficiaryProtector;

    /// <summary>Số lần nhập sai khóa mở gói tối đa trước khi khóa lượt.</summary>
    public const int MaxKeyFailures = 5;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IHandoverRepository handover,
        ITokenService tokens,
        IHandoverPolicy policy,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
        : this(currentUser, users, vaults, handover, tokens, policy, new NoHoldReader(), unitOfWork, timeProvider)
    {
    }

    public HandoverService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IHandoverRepository handover,
        ITokenService tokens,
        IHandoverPolicy policy,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IExecutorKeyShardRepository? keyShards = null,
        IHandoverSubmissionRepository? submissions = null,
        IBeneficiaryRepository? beneficiaries = null,
        IBeneficiaryDataProtector? beneficiaryProtector = null)
    {
        this.beneficiaries = beneficiaries;
        this.beneficiaryProtector = beneficiaryProtector;
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.handover = handover;
        this.tokens = tokens;
        this.policy = policy;
        this.holdReader = holdReader;
        this.keyShards = keyShards;
        this.submissions = submissions;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<InvitationStatusResponse> GetInvitationAsync(string token, CancellationToken ct)
    {
        HandoverTurn turn = await FindTurnByTokenAsync(token, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();

        bool responseExpired = turn.Status == HandoverTurnStatus.InitialResponseExpired;
        if (turn.Status == HandoverTurnStatus.Invited && HandoverRules.IsExpired(turn.ResponseDeadline, now))
        {
            responseExpired = true;
        }

        InvitationStatusResponse response = new InvitationStatusResponse();
        response.Valid = true;
        response.ResponseExpired = responseExpired;

        // Chỉ trả email đã che bớt và cờ "đã có tài khoản": link công khai không được lộ email đầy đủ.
        string? invitedEmail = await FindRecipientEmailAsync(turn, ct);
        if (invitedEmail != null)
        {
            response.InvitedEmailMasked = MaskEmail(invitedEmail);
            response.InvitedEmail = invitedEmail;
            User? existing = await users.FindByEmailAsync(invitedEmail, ct);
            response.AccountExists = existing != null;
        }

        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault != null)
        {
            User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
            if (owner != null)
            {
                response.OwnerFullName = owner.FullName;
            }
        }
        return response;
    }

    public async Task<HandoverTurnResponse> ClaimAsync(string token, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User user = await EnsureAccountUsableAsync(userId, ct);

        HandoverTurn turn = await FindTurnByTokenAsync(token, ct);
        return await ClaimTurnAsync(user, turn, ct);
    }

    /// <summary>
    /// Nhận lượt ngay trong app (không qua link): FE gọi khi người nhận bấm "Đồng ý nhận" / "Từ chối nhận" ở thẻ thư mời.
    /// Dùng chung mọi bước kiểm với nhận bằng link (ClaimTurnAsync).
    /// </summary>
    public async Task<HandoverTurnResponse> ClaimByTurnAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User user = await EnsureAccountUsableAsync(userId, ct);

        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null)
        {
            throw HandoverNotFound();
        }
        return await ClaimTurnAsync(user, turn, ct);
    }

    /// <summary>
    /// Lời mời nhận gói đang chờ tài khoản này: lượt chưa ai nhận, còn nhận được, kế hoạch đang bàn giao, kho không bị giữ,
    /// không phải kho của chính mình và email người nhận trùng email đã xác minh của tài khoản (D2).
    /// </summary>
    /// <remarks>
    /// Email người nhận nằm trong dữ liệu mã hóa (Beneficiary.ContactEncrypted), không có cột băm để tra, nên phải lấy các lượt
    /// chưa nhận của mọi kế hoạch đang bàn giao rồi giải mã từng người nhận để so email. Chấp nhận được ở quy mô đồ án
    /// (ít kế hoạch đang bàn giao cùng lúc); nếu lớn hơn thì cần thêm cột băm email (phải có migration). Không log email.
    /// </remarks>
    public async Task<List<PendingInvitationResponse>> GetMyInvitationsAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired,
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }

        List<PendingInvitationResponse> result = new List<PendingInvitationResponse>();
        if (user.Status != UserStatus.Active || user.EmailVerifiedAt == null)
        {
            return result;
        }

        string accountEmail = NormalizeEmail(user.Email);
        List<HandoverTurn> candidates = await handover.FindUnclaimedTurnsInHandoverAsync(ct);
        foreach (HandoverTurn turn in candidates)
        {
            if (!HandoverRules.CanClaim(turn.Status))
            {
                continue;
            }

            Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
            if (vault == null || vault.OwnerId == userId)
            {
                continue;
            }

            if (vault.Plan.Status != PlanStatus.HandoverInProgress)
            {
                continue;
            }

            if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
            {
                continue;
            }

            string? invitedEmail = await FindRecipientEmailAsync(turn, ct);
            if (invitedEmail == null || invitedEmail != accountEmail)
            {
                continue;
            }

            User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
            string ownerName = string.Empty;
            if (owner != null)
            {
                ownerName = owner.FullName;
            }

            PendingInvitationResponse item = new PendingInvitationResponse();
            item.TurnId = turn.Id;
            item.ContainerName = turn.ContainerName;
            item.OwnerFullName = ownerName;
            item.InvitedAt = turn.InvitedAt;
            result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// Các bước kiểm và ghi chung cho nhận bằng token và nhận bằng turnId (turn đã tìm được, user đã qua EnsureAccountUsableAsync).
    /// </summary>
    private async Task<HandoverTurnResponse> ClaimTurnAsync(User user, HandoverTurn turn, CancellationToken ct)
    {
        Guid userId = user.Id;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Cùng người bấm lại (F5, mở link lần hai): trả trạng thái hiện tại, không ghi gì.
        if (turn.ClaimantUserId == userId)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }

        if (turn.ClaimantUserId != null)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.HandoverAlreadyClaimed,
                "Lời mời này đã được một tài khoản khác nhận.");
        }

        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw InvitationInvalid();
        }

        if (vault.OwnerId == userId)
        {
            throw NotAllowed("OWNER_CANNOT_CLAIM", "Chủ kho không thể nhận tài sản của chính mình.");
        }

        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }

        if (!HandoverRules.CanClaim(turn.Status))
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không còn nhận được.");
        }

        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa nhận được lượt này.");
        }

        // D2: đúng email mới được nhận (tài khoản đã xác minh email ở EnsureAccountUsableAsync).
        await EnsureEmailMatchesAsync(user, turn, ct);

        turn.ClaimantUserId = userId;
        turn.ClaimedAt = now;
        turn.UpdatedAt = now;
        // Chạm kế hoạch để RowVersion của nó chặn nhận lượt chạy cùng lúc với báo còn sống (ALIVE-01).
        vault.Plan.UpdatedAt = now;

        if (policy.RequireIdentityVerification)
        {
            turn.Status = HandoverTurnStatus.IdentityInProgress;
        }
        else
        {
            // Công tắc Development: bỏ qua xác minh, đánh dấu để giao diện báo rõ.
            turn.IdentityBypassed = true;
            HandoverRules.MarkReadyToAccept(turn, now);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await BuildTurnResponseAsync(turn, ct);
    }

    public async Task<List<HandoverTurnResponse>> GetMyTurnsAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<HandoverTurn> turns = await handover.FindTurnsByClaimantAsync(userId, ct);

        List<HandoverTurnResponse> responses = new List<HandoverTurnResponse>();
        foreach (HandoverTurn turn in turns)
        {
            responses.Add(await BuildTurnResponseAsync(turn, ct));
        }
        return responses;
    }

    public async Task<HandoverTurnResponse> GetTurnAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);
        return await BuildTurnResponseAsync(turn, ct);
    }

    public async Task<HandoverTurnResponse> AcceptAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);

        // Bấm lặp: đã đồng ý rồi thì trả lại đúng grant cũ, không tạo thêm và không ghi gì.
        if (turn.Status == HandoverTurnStatus.HandoverCommitted && turn.Grant != null)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverPlan plan = await EnsureDecidableAsync(turn, now, ct);
        // Khóa mở gói chỉ cần khi tải (HandoverDownloadService), không chặn việc đồng ý nhận.
        plan.UpdatedAt = now;

        HandoverGrant grant = HandoverRules.CommitAccept(turn, userId, now);
        handover.AddGrant(grant);

        await CloseDecidedPlanAsync(plan, now, ct);

        // RowVersion của lượt và chỉ mục unique của grant chặn hai lần đồng ý chạy song song:
        // bên thua nhận 409 và khi bấm lại sẽ thấy lượt đã hoàn tất (nhánh "bấm lặp" ở đầu hàm).
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (handover.IsUniqueViolation(ex))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }
        return await BuildTurnResponseAsync(turn, ct);
    }

    public async Task<HandoverTurnResponse> DeclineAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);

        if (turn.Status == HandoverTurnStatus.Declined)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverPlan plan = await EnsureDecidableAsync(turn, now, ct);
        plan.UpdatedAt = now;

        // Người nhận từ chối thì nội dung của lượt này bị xóa luôn thay vì chờ hết thời hạn lưu (xem MarkDeclined).
        HandoverRules.MarkDeclined(turn, now);

        await CloseDecidedPlanAsync(plan, now, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return await BuildTurnResponseAsync(turn, ct);
    }

    public async Task<List<HandoverItemResponse>> ListAssetsAsync(Guid grantId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();

        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        if (grant == null || grant.UserId != userId)
        {
            throw HandoverNotFound();
        }

        HandoverTurn turn = grant.Turn;
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw HandoverNotFound();
        }

        string? deniedReason = HandoverRules.GrantDeniedReason(grant, vault.Plan.Status, timeProvider.GetUtcNow());
        if (deniedReason == null && await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            deniedReason = "HOLD_ACTIVE";
        }
        if (deniedReason != null)
        {
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = deniedReason;
            throw new AppException(AppErrorType.Forbidden, ErrorCode.GrantNotActive,
                "Quyền xem tài sản không còn hiệu lực.", extensions);
        }

        List<HandoverItemResponse> items = new List<HandoverItemResponse>();
        foreach (HandoverSnapshotItem item in turn.Items.OrderBy(i => i.Position))
        {
            items.Add(ToItemResponse(item));
        }
        return items;
    }

    /// <summary>Kiểm lượt đang chờ quyết định, kế hoạch đang bàn giao và chưa quá hạn quyết định.</summary>
    private async Task<HandoverPlan> EnsureDecidableAsync(HandoverTurn turn, DateTimeOffset now, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw HandoverNotFound();
        }

        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }

        if (turn.Status != HandoverTurnStatus.ReadyToAccept)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này chưa sẵn sàng để quyết định.");
        }

        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa quyết định được.");
        }

        // Đúng giây hạn là hết hạn, kể cả khi job chưa kịp chạy (TIME-02).
        if (turn.DecisionDeadline != null && HandoverRules.IsExpired(turn.DecisionDeadline.Value, now))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.HandoverDecisionExpired,
                "Đã quá hạn quyết định.");
        }

        return vault.Plan;
    }

    /// <summary>Sau khi lượt này kết thúc, đóng kế hoạch nếu mọi lượt khác cũng đã kết thúc.</summary>
    private async Task CloseDecidedPlanAsync(HandoverPlan plan, DateTimeOffset now, CancellationToken ct)
    {
        List<HandoverTurn> turns = await handover.FindTurnsByPlanAsync(plan.Id, ct);
        HandoverRules.CloseIfAllTerminal(plan, turns, now);
    }

    private async Task<User> EnsureAccountUsableAsync(Guid userId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired,
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive,
                "Tài khoản đang bị khóa hoặc vô hiệu hóa.");
        }

        if (user.EmailVerifiedAt == null)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.EmailNotVerified,
                "Vui lòng xác minh email trước khi nhận tài sản.");
        }

        return user;
    }

    /// <summary>
    /// D2: email tài khoản (chuẩn hóa) phải trùng email người nhận của lượt. Sai thì 403 HANDOVER_EMAIL_MISMATCH kèm email được mời
    /// đã che bớt, không lộ email đầy đủ.
    /// </summary>
    private async Task EnsureEmailMatchesAsync(User user, HandoverTurn turn, CancellationToken ct)
    {
        string? invitedEmail = await FindRecipientEmailAsync(turn, ct);
        string accountEmail = NormalizeEmail(user.Email);
        if (invitedEmail != null && invitedEmail == accountEmail)
        {
            return;
        }

        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        if (invitedEmail != null)
        {
            extensions["invitedEmailMasked"] = MaskEmail(invitedEmail);
        }
        throw new AppException(AppErrorType.Forbidden, ErrorCode.HandoverEmailMismatch,
            "Lời mời này gửi tới một email khác. Hãy đăng nhập bằng đúng email được mời.", extensions);
    }

    /// <summary>Email người nhận của lượt (giải mã, chuẩn hóa trim + chữ thường); không có hoặc không đọc được thì null.</summary>
    private async Task<string?> FindRecipientEmailAsync(HandoverTurn turn, CancellationToken ct)
    {
        if (beneficiaries == null || beneficiaryProtector == null)
        {
            return null;
        }
        try
        {
            Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
            if (beneficiary == null)
            {
                return null;
            }
            string? email = beneficiaryProtector.Unprotect(beneficiary).Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }
            return NormalizeEmail(email);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>Che email dạng m•••••h@example.com: giữ ký tự đầu và cuối của phần tên, giữ nguyên tên miền.</summary>
    private static string MaskEmail(string email)
    {
        int atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "•••";
        }

        string localPart = email.Substring(0, atIndex);
        string domain = email.Substring(atIndex);
        if (localPart.Length <= 2)
        {
            return localPart.Substring(0, 1) + "•••" + domain;
        }

        string hidden = new string('•', localPart.Length - 2);
        string lastChar = localPart.Substring(localPart.Length - 1);
        return localPart.Substring(0, 1) + hidden + lastChar + domain;
    }

    private async Task<HandoverTurn> FindTurnByTokenAsync(string token, CancellationToken ct)
    {
        string hash = tokens.HashOpaqueToken(token);
        HandoverTurn? turn = await handover.FindTurnByTokenHashAsync(hash, ct);
        if (turn == null)
        {
            throw InvitationInvalid();
        }
        return turn;
    }

    /// <summary>Lượt của người đang đăng nhập; id lạ và lượt của người khác cho cùng một lỗi (SEC-04).</summary>
    private async Task<HandoverTurn> FindMyTurnAsync(Guid turnId, Guid userId, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.ClaimantUserId != userId)
        {
            throw HandoverNotFound();
        }
        return turn;
    }

    /// <summary>Họ tên người nhận Chủ kho đã nhập, giải mã để chào đúng người; không đọc được thì để trống, không làm hỏng màn hình.</summary>
    private async Task<string?> FindRecipientFullNameAsync(HandoverTurn turn, CancellationToken ct)
    {
        if (beneficiaries == null || beneficiaryProtector == null)
        {
            return null;
        }
        try
        {
            Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
            if (beneficiary == null)
            {
                return null;
            }
            string name = beneficiaryProtector.Unprotect(beneficiary).FullName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            return name.Trim();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private async Task<HandoverTurnResponse> BuildTurnResponseAsync(HandoverTurn turn, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        HandoverTurnResponse response = new HandoverTurnResponse();
        response.Id = turn.Id;
        response.Status = turn.Status.ToApiString();
        response.ServerTime = now;
        response.InvitedAt = turn.InvitedAt;
        response.ResponseDeadline = turn.ResponseDeadline;
        response.DecisionDeadline = turn.DecisionDeadline;
        response.IdentityBypassed = turn.IdentityBypassed;
        response.IdentityConfirmedByExecutor = turn.IdentityConfirmedByUserId != null;
        response.KeyRequired = await IsKeyRequiredAsync(turn, ct);
        response.KeyVerified = !response.KeyRequired || turn.KeyVerifiedAt != null;
        response.ReceivedAllAt = turn.ReceivedAllAt;
        response.RecipientFullName = await FindRecipientFullNameAsync(turn, ct);
        if (submissions != null)
        {
            var reviewer = await submissions.FindApprovingVerifierAsync(turn.VaultId, ct);
            if (reviewer != null)
            {
                HandoverReviewerResponse reviewerResponse = new HandoverReviewerResponse();
                reviewerResponse.FullName = reviewer.Value.FullName;
                reviewerResponse.LawyerCardNumber = reviewer.Value.LawyerCardNumber;
                response.Reviewer = reviewerResponse;
            }
        }

        bool showContent = turn.Status == HandoverTurnStatus.ReadyToAccept
            || turn.Status == HandoverTurnStatus.HandoverCommitted;
        if (showContent)
        {
            HandoverContainerResponse container = new HandoverContainerResponse();
            container.Name = turn.ContainerName;
            response.Container = container;

            foreach (HandoverSnapshotItem item in turn.Items.OrderBy(i => i.Position))
            {
                response.Items.Add(ToItemResponse(item));
            }
        }

        if (turn.Grant != null)
        {
            HandoverGrantResponse grant = new HandoverGrantResponse();
            grant.Id = turn.Grant.Id;
            grant.Status = HandoverRules.EffectiveGrantStatus(turn.Grant, now).ToApiString();
            grant.ExpiresAt = turn.Grant.ExpiresAt;
            response.Grant = grant;
        }

        return response;
    }

    /// <summary>Kho đã cấp khóa mở gói thì người nhận phải nhập khóa; chưa cấp thì bỏ qua.</summary>
    private async Task<bool> IsKeyRequiredAsync(HandoverTurn turn, CancellationToken ct)
    {
        if (keyShards == null)
        {
            return false;
        }
        return await keyShards.FindByVaultIdAsync(turn.VaultId, ct) != null;
    }

    public async Task<HandoverTurnResponse> ConfirmReceivedAllAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);
        if (turn.Status != HandoverTurnStatus.HandoverCommitted || turn.Grant == null)
        {
            throw NotAllowed("NOT_COMMITTED", "Chỉ xác nhận đã nhận đủ sau khi đồng ý nhận tài sản.");
        }
        if (turn.ReceivedAllAt == null)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            turn.ReceivedAllAt = now;
            turn.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await BuildTurnResponseAsync(turn, ct);
    }

    public async Task<HandoverTurnResponse> VerifyKeyAsync(Guid turnId, string? key, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);
        ExecutorKeyShard? shard = null;
        if (keyShards != null)
        {
            shard = await keyShards.FindByVaultIdAsync(turn.VaultId, ct);
        }
        if (shard == null || turn.KeyVerifiedAt != null)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }
        if (turn.Status != HandoverTurnStatus.ReadyToAccept && turn.Status != HandoverTurnStatus.HandoverCommitted)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này chưa sẵn sàng để nhập khóa.");
        }
        if (turn.KeyFailedCount >= MaxKeyFailures)
        {
            throw new AppException(AppErrorType.Locked, ErrorCode.HandoverKeyLocked,
                "Đã nhập sai khóa quá nhiều lần. Hãy liên hệ Người thực thi hoặc quản trị viên.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (ExecutorKeyRules.Matches(turn.VaultId, shard.Version, key ?? string.Empty, shard.ShardHash))
        {
            turn.KeyVerifiedAt = now;
            turn.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
            return await BuildTurnResponseAsync(turn, ct);
        }

        // Lưu bộ đếm rồi mới báo lỗi, nên lần thử sai không bị mất khi request kết thúc bằng exception.
        turn.KeyFailedCount = turn.KeyFailedCount + 1;
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["attemptsRemaining"] = Math.Max(0, MaxKeyFailures - turn.KeyFailedCount);
        throw new AppException(AppErrorType.Forbidden, ErrorCode.HandoverKeyInvalid, "Khóa mở gói chưa đúng.", extensions);
    }

    private static HandoverItemResponse ToItemResponse(HandoverSnapshotItem item)
    {
        HandoverItemResponse response = new HandoverItemResponse();
        response.Id = item.Id;
        response.Name = item.Name;
        response.Kind = item.Kind.ToApiString();
        response.FileName = item.FileName;
        response.ContentType = item.ContentType;
        response.SizeBytes = item.SizeBytes;
        return response;
    }

    private static AppException InvitationInvalid()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverInvitationInvalid,
            "Liên kết mời không hợp lệ.");
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
