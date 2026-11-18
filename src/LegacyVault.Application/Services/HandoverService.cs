using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

// Cổng người nhận, các nút theo thứ tự người nhận đi:
//   1. Mở link trong thư mời ............ GetInvitationAsync
//   2. Bấm "Nhận lời mời" ............... ClaimAsync (qua link), ClaimByTurnAsync (trong app)
//   3. Xem lời mời và lượt của mình ..... GetMyInvitationsAsync, GetMyTurnsAsync, GetTurnAsync
//   4. Nhập khóa mở gói ................. VerifyKeyAsync
//   5. Xem danh sách tài sản ............ ListAssetsAsync
//   6. Báo đã nhận đủ ................... ConfirmReceivedAllAsync
// Luồng cũ, chỉ cho lượt không có biên bản: AcceptAsync (đồng ý nhận), DeclineAsync (từ chối nhận).
// Luồng Biên Bản kết thúc lượt ở HandoverSubmissionReviewService.CompleteAsync, không đi qua hai hàm này.
// Các hàm private ở cuối tệp là phần kiểm và dựng kết quả dùng chung.
public sealed class HandoverService : IHandoverService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly ITokenService tokens;
    private readonly IHandoverPolicy policy;
    private readonly IHoldReader holdReader;
    private readonly IExecutorKeyShardRepository keyShards;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;

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
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IExecutorKeyShardRepository keyShards,
        IHandoverSubmissionRepository submissions,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector beneficiaryProtector)
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

    /// <summary>Bước 1: người nhận mở link trong thư mời (không cần đăng nhập).</summary>
    public async Task<InvitationStatusResponse> GetInvitationAsync(string token, CancellationToken ct)
    {
        // 1. Tìm lượt theo token trong link; token sai thì báo link không hợp lệ
        HandoverTurn turn = await FindTurnByTokenAsync(token, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();

        // 2. Đã quá hạn phản hồi ban đầu chưa (quá hạn vẫn nhận được, chỉ để giao diện báo)
        bool responseExpired = turn.Status == HandoverTurnStatus.InitialResponseExpired;
        if (turn.Status == HandoverTurnStatus.Invited && HandoverRules.IsExpired(turn.ResponseDeadline, now))
        {
            responseExpired = true;
        }

        // 3. Dựng kết quả: link hợp lệ, email được mời, đã có tài khoản chưa, tên Chủ kho
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

    /// <summary>Bước 2: người nhận đã đăng nhập bấm "Nhận lời mời" trên trang mở từ link.</summary>
    public async Task<HandoverTurnResponse> ClaimAsync(string token, CancellationToken ct)
    {
        // 1. Người đang đăng nhập; tài khoản phải đang hoạt động và đã xác minh email
        Guid userId = currentUser.RequireUserId();
        User user = await EnsureAccountUsableAsync(userId, ct);

        // 2. Tìm lượt theo token trong link
        HandoverTurn turn = await FindTurnByTokenAsync(token, ct);

        // 3. Kiểm điều kiện, gắn lượt cho người này rồi lưu (xem ClaimTurnAsync)
        return await ClaimTurnAsync(user, turn, ct);
    }

    /// <summary>
    /// Nhận lượt ngay trong app (không qua link): FE gọi khi người nhận bấm "Đồng ý nhận" / "Từ chối nhận" ở thẻ thư mời.
    /// Dùng chung mọi bước kiểm với nhận bằng link (ClaimTurnAsync).
    /// </summary>
    public async Task<HandoverTurnResponse> ClaimByTurnAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Người đang đăng nhập; tài khoản phải đang hoạt động và đã xác minh email
        Guid userId = currentUser.RequireUserId();
        User user = await EnsureAccountUsableAsync(userId, ct);

        // 2. Tìm lượt theo id
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null)
        {
            throw HandoverAccess.NotFound();
        }

        // 3. Kiểm điều kiện, gắn lượt cho người này rồi lưu (xem ClaimTurnAsync)
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
        // 1. Người đang đăng nhập; tài khoản chưa dùng được thì trả danh sách rỗng
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

        // 2. Lấy mọi lượt chưa ai nhận, rồi bỏ qua lượt không hợp lệ:
        //    không nhận được nữa, kho không có hoặc là kho của mình, kế hoạch không ở giai đoạn bàn giao,
        //    kho đang bị giữ, hoặc email người nhận khác email tài khoản
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

            // 3. Lượt hợp lệ: thêm vào danh sách kèm tên gói và tên Chủ kho
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

        // 1. Cùng người bấm lại (F5, mở link lần hai): trả trạng thái hiện tại, không ghi gì.
        if (turn.ClaimantUserId == userId)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }

        // 2. Các điều kiện chặn, mỗi điều kiện một lỗi riêng
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
            throw HandoverAccess.NotAllowed("OWNER_CANNOT_CLAIM", "Chủ kho không thể nhận tài sản của chính mình.");
        }

        // Tài khoản nhân sự (Executor, Verifier, Admin) xử lý hồ sơ bàn giao nên không được nhận tài sản.
        if (user.Role == UserRole.Executor || user.Role == UserRole.Verifier || user.Role == UserRole.Admin)
        {
            throw HandoverAccess.NotAllowed("STAFF_CANNOT_CLAIM", "Tài khoản nhân sự không thể nhận tài sản bàn giao.");
        }

        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw HandoverAccess.NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }

        if (!HandoverRules.CanClaim(turn.Status))
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này không còn nhận được.");
        }

        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw HandoverAccess.NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa nhận được lượt này.");
        }

        // D2: đúng email mới được nhận (tài khoản đã xác minh email ở EnsureAccountUsableAsync).
        await EnsureEmailMatchesAsync(user, turn, ct);

        // 3. Gắn lượt cho người này
        turn.ClaimantUserId = userId;
        turn.ClaimedAt = now;
        turn.UpdatedAt = now;
        // Chạm kế hoạch để RowVersion của nó chặn nhận lượt chạy cùng lúc với báo còn sống (ALIVE-01).
        vault.Plan.UpdatedAt = now;

        // 4. Chuyển lượt sang bước xác minh danh tính (người nhận nộp hồ sơ và biên bản)
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

        // 5. Lưu một lần rồi trả trạng thái mới của lượt
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildTurnResponseAsync(turn, ct);
    }

    /// <summary>Bước 3: danh sách lượt người đang đăng nhập đã nhận.</summary>
    public async Task<List<HandoverTurnResponse>> GetMyTurnsAsync(CancellationToken ct)
    {
        // 1. Người đang đăng nhập và các lượt họ đã nhận
        Guid userId = currentUser.RequireUserId();
        List<HandoverTurn> turns = await handover.FindTurnsByClaimantAsync(userId, ct);

        // 2. Dựng kết quả cho từng lượt
        List<HandoverTurnResponse> responses = new List<HandoverTurnResponse>();
        foreach (HandoverTurn turn in turns)
        {
            responses.Add(await BuildTurnResponseAsync(turn, ct));
        }
        return responses;
    }

    /// <summary>Bước 3: chi tiết một lượt của người đang đăng nhập.</summary>
    public async Task<HandoverTurnResponse> GetTurnAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập; không thì báo không tìm thấy
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);

        // 2. Trả trạng thái hiện tại của lượt
        return await BuildTurnResponseAsync(turn, ct);
    }

    /// <summary>Luồng cũ: người nhận bấm "Đồng ý nhận" khi lượt đang chờ quyết định (lượt không có biên bản).</summary>
    public async Task<HandoverTurnResponse> AcceptAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);

        // 2. Bấm lặp: đã đồng ý rồi thì trả lại đúng grant cũ, không tạo thêm và không ghi gì.
        if (turn.Status == HandoverTurnStatus.HandoverCommitted && turn.Grant != null)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }

        // 3. Kiểm lượt còn quyết định được (xem EnsureDecidableAsync)
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverPlan plan = await EnsureDecidableAsync(turn, now, ct);
        // Khóa mở gói chỉ cần khi tải (HandoverDownloadService), không chặn việc đồng ý nhận.
        plan.UpdatedAt = now;

        // 4. Tạo quyền tải cho người nhận, kết thúc lượt; đóng kế hoạch nếu mọi lượt đã xong
        HandoverGrant grant = HandoverRules.CommitAccept(turn, userId, now);
        handover.AddGrant(grant);

        await CloseDecidedPlanAsync(plan, now, ct);

        // 5. Lưu một lần.
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

    /// <summary>Luồng cũ: người nhận bấm "Từ chối nhận" khi lượt đang chờ quyết định (lượt không có biên bản).</summary>
    public async Task<HandoverTurnResponse> DeclineAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);

        // 2. Bấm lặp: đã từ chối rồi thì trả trạng thái hiện tại
        if (turn.Status == HandoverTurnStatus.Declined)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }

        // 3. Kiểm lượt còn quyết định được (xem EnsureDecidableAsync)
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverPlan plan = await EnsureDecidableAsync(turn, now, ct);
        plan.UpdatedAt = now;

        // 4. Kết thúc lượt bằng từ chối; đóng kế hoạch nếu mọi lượt đã xong.
        // Người nhận từ chối thì nội dung của lượt này bị xóa luôn thay vì chờ hết thời hạn lưu (xem MarkDeclined).
        HandoverRules.MarkDeclined(turn, now);

        await CloseDecidedPlanAsync(plan, now, ct);

        // 5. Lưu một lần rồi trả trạng thái mới của lượt
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildTurnResponseAsync(turn, ct);
    }

    /// <summary>Bước 5: người nhận xem danh sách tài sản của gói được bàn giao.</summary>
    public async Task<List<HandoverItemResponse>> ListAssetsAsync(Guid grantId, CancellationToken ct)
    {
        // 1. Người đang đăng nhập
        Guid userId = currentUser.RequireUserId();

        // 2. Tìm quyền xem; quyền của người khác thì coi như không có
        HandoverGrant? grant = await handover.FindGrantByIdAsync(grantId, ct);
        if (grant == null || grant.UserId != userId)
        {
            throw HandoverAccess.NotFound();
        }

        HandoverTurn turn = grant.Turn;
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw HandoverAccess.NotFound();
        }

        // 3. Quyền còn hiệu lực không: hết hạn, bị thu hồi, kế hoạch không còn chạy, hoặc kho đang bị giữ
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

        // 4. Trả danh sách tài sản theo thứ tự trong gói
        List<HandoverItemResponse> items = new List<HandoverItemResponse>();
        foreach (HandoverSnapshotItem item in turn.Items.OrderBy(i => i.Position))
        {
            items.Add(item.ToResponse());
        }
        return items;
    }

    /// <summary>Kiểm lượt đang chờ quyết định, kế hoạch đang bàn giao và chưa quá hạn quyết định.</summary>
    private async Task<HandoverPlan> EnsureDecidableAsync(HandoverTurn turn, DateTimeOffset now, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null)
        {
            throw HandoverAccess.NotFound();
        }

        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw HandoverAccess.NotAllowed("PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }

        if (turn.Status != HandoverTurnStatus.ReadyToAccept)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này chưa sẵn sàng để quyết định.");
        }

        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw HandoverAccess.NotAllowed("HOLD_ACTIVE", "Kho đang bị giữ nên chưa quyết định được.");
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

    /// <summary>Họ tên người nhận Chủ kho đã nhập, giải mã để chào đúng người; không đọc được thì để trống, không làm hỏng màn hình.</summary>
    private async Task<string?> FindRecipientFullNameAsync(HandoverTurn turn, CancellationToken ct)
    {
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
        var reviewer = await submissions.FindApprovingVerifierAsync(turn.VaultId, ct);
        if (reviewer != null)
        {
            HandoverReviewerResponse reviewerResponse = new HandoverReviewerResponse();
            reviewerResponse.FullName = reviewer.Value.FullName;
            reviewerResponse.LawyerCardNumber = reviewer.Value.LawyerCardNumber;
            response.Reviewer = reviewerResponse;
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
                response.Items.Add(item.ToResponse());
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
        return await keyShards.FindByVaultIdAsync(turn.VaultId, ct) != null;
    }

    /// <summary>Bước 6: người nhận bấm "Đã nhận đủ" sau khi tải xong.</summary>
    public async Task<HandoverTurnResponse> ConfirmReceivedAllAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);

        // 2. Chỉ xác nhận được khi lượt đã bàn giao và có quyền tải
        if (turn.Status != HandoverTurnStatus.HandoverCommitted || turn.Grant == null)
        {
            throw HandoverAccess.NotAllowed("NOT_COMMITTED", "Chỉ xác nhận đã nhận đủ sau khi đồng ý nhận tài sản.");
        }
        // 3. Ghi mốc nhận đủ một lần; bấm lặp thì giữ mốc cũ
        if (turn.ReceivedAllAt == null)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            turn.ReceivedAllAt = now;
            turn.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await BuildTurnResponseAsync(turn, ct);
    }

    /// <summary>Bước 4: người nhận nhập khóa mở gói lấy từ Người thực thi.</summary>
    public async Task<HandoverTurnResponse> VerifyKeyAsync(Guid turnId, string? key, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);

        // 2. Kho chưa cấp khóa, hoặc đã nhập đúng từ trước: không cần làm gì
        ExecutorKeyShard? shard = await keyShards.FindByVaultIdAsync(turn.VaultId, ct);
        if (shard == null || turn.KeyVerifiedAt != null)
        {
            return await BuildTurnResponseAsync(turn, ct);
        }
        // 3. Lượt phải đã sẵn sàng hoặc đã bàn giao, và chưa bị khóa vì sai quá số lần
        if (turn.Status != HandoverTurnStatus.ReadyToAccept && turn.Status != HandoverTurnStatus.HandoverCommitted)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Lượt này chưa sẵn sàng để nhập khóa.");
        }
        if (turn.KeyFailedCount >= MaxKeyFailures)
        {
            throw new AppException(AppErrorType.Locked, ErrorCode.HandoverKeyLocked,
                "Đã nhập sai khóa quá nhiều lần. Hãy liên hệ Người thực thi hoặc quản trị viên.");
        }

        // 4. Khóa đúng: ghi mốc đã nhập khóa, lưu rồi trả trạng thái mới
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (ExecutorKeyRules.Matches(turn.VaultId, shard.Version, key ?? string.Empty, shard.ShardHash))
        {
            turn.KeyVerifiedAt = now;
            turn.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
            return await BuildTurnResponseAsync(turn, ct);
        }

        // 5. Khóa sai: tăng bộ đếm sai, lưu, rồi báo lỗi kèm số lần thử còn lại.
        // Lưu bộ đếm rồi mới báo lỗi, nên lần thử sai không bị mất khi request kết thúc bằng exception.
        turn.KeyFailedCount = turn.KeyFailedCount + 1;
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["attemptsRemaining"] = Math.Max(0, MaxKeyFailures - turn.KeyFailedCount);
        throw new AppException(AppErrorType.Forbidden, ErrorCode.HandoverKeyInvalid, "Khóa mở gói chưa đúng.", extensions);
    }

    private static AppException InvitationInvalid()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverInvitationInvalid,
            "Liên kết mời không hợp lệ.");
    }
}
