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
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.handover = handover;
        this.tokens = tokens;
        this.policy = policy;
        this.holdReader = holdReader;
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
        return response;
    }

    public async Task<HandoverTurnResponse> ClaimAsync(string token, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        await EnsureAccountUsableAsync(userId, ct);

        HandoverTurn turn = await FindTurnByTokenAsync(token, ct);
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
        plan.UpdatedAt = now;

        HandoverGrant grant = new HandoverGrant();
        grant.TurnId = turn.Id;
        grant.Turn = turn;
        grant.UserId = userId;
        grant.Status = HandoverGrantStatus.Active;
        grant.GrantedAt = now;
        grant.ExpiresAt = now + HandoverRules.GrantWindow;
        handover.AddGrant(grant);
        turn.Grant = grant;

        turn.Status = HandoverTurnStatus.HandoverCommitted;
        turn.DecidedAt = now;
        turn.EndedAt = now;
        turn.UpdatedAt = now;

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

        turn.Status = HandoverTurnStatus.Declined;
        turn.DecidedAt = now;
        turn.EndedAt = now;
        turn.UpdatedAt = now;

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

    private async Task EnsureAccountUsableAsync(Guid userId, CancellationToken ct)
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

    private Task<HandoverTurnResponse> BuildTurnResponseAsync(HandoverTurn turn, CancellationToken ct)
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

        return Task.FromResult(response);
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
