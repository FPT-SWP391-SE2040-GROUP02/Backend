using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Kiểm hold khi phát hành: dùng ở duyệt chứng tử, nhận lượt, đồng ý, tải, kích hoạt và dọn dữ liệu.</summary>
public interface IHoldReader
{
    Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct);
}

/// <summary>Bản không có hold nào, dùng khi dựng service ở test cũ không nối phần hold.</summary>
public sealed class NoHoldReader : IHoldReader
{
    public Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(false);
    }
}

public sealed class HoldReader : IHoldReader
{
    private readonly IHoldRepository holds;

    public HoldReader(IHoldRepository holds)
    {
        this.holds = holds;
    }

    public Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct)
    {
        return holds.HasActiveHoldAsync(vaultId, ct);
    }
}

public interface IHoldService
{
    /// <summary>
    /// Nội bộ (F2C): đặt hold OwnerAlive cho kho nếu chưa có. KHÔNG SaveChanges; người gọi đã chạm plan.UpdatedAt.
    /// responsibleUserId null → vẫn tạo, sự cố đi kèm lo việc tìm người.
    /// </summary>
    Task PlaceOwnerAliveAsync(Vault vault, Guid? responsibleUserId, DateTimeOffset reviewAt, CancellationToken ct);

    /// <summary>Nội bộ (F2C): gỡ hold OwnerAlive của kho nếu có. KHÔNG SaveChanges.</summary>
    Task ReleaseOwnerAliveAsync(Guid vaultId, Guid? byUserId, string reason, CancellationToken ct);

    /// <summary>
    /// Nội bộ (F2C, BR-76 c): dời ngày xem lại của hold OwnerAlive đang có theo hạn selfie mới khi Verifier yêu cầu Owner
    /// chụp lại. Không có hold thì bỏ qua. KHÔNG SaveChanges.
    /// </summary>
    Task RescheduleOwnerAliveAsync(Guid vaultId, DateTimeOffset reviewAt, CancellationToken ct);

    Task<PaginatedList<HoldResponse>> ListAsync(string? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct);

    Task<HoldResponse> GetAsync(Guid holdId, CancellationToken ct);

    /// <summary>Admin đặt hold thủ công trên kho hoặc gói. Lỗi: 400, 404 VAULT_NOT_FOUND / CONTAINER_NOT_FOUND.</summary>
    Task<HoldResponse> PlaceAsync(PlaceHoldRequest request, CancellationToken ct);

    /// <summary>Admin gỡ hold thủ công. Hold OwnerAlive chỉ gỡ qua kết luận của Verifier (ALIVE-05).</summary>
    Task<HoldResponse> ReleaseAsync(Guid holdId, ReleaseHoldRequest request, CancellationToken ct);

    /// <summary>Ghi nhận đã xem lại hold và đặt ngày xem lại tiếp theo (tối đa 7 ngày).</summary>
    Task<HoldResponse> ReviewAsync(Guid holdId, ReviewHoldRequest request, CancellationToken ct);
}

/// <remarks>
/// - Hold luôn có lý do, người phụ trách và ngày xem lại tối đa 7 ngày (SRS §6.4).
/// - Đặt hoặc gỡ ghi plan.UpdatedAt trong cùng giao dịch: nhận lượt, đồng ý, duyệt chạy cùng lúc thua RowVersion của kế hoạch.
/// - Không có thao tác nào ở đây đọc nội dung tài sản; Admin không mở kho (ASSIGN-05).
/// - Gỡ hold (hết hold của kho) thì dời hạn phản hồi, hạn quyết định và hạn grant đúng bằng thời gian đã dừng (TIME-04, H1).
/// </remarks>
public sealed class HoldService : IHoldService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IContainerRepository containers;
    private readonly IHoldRepository holds;
    private readonly IObjectionRepository objections;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HoldService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IContainerRepository containers,
        IHoldRepository holds,
        IObjectionRepository objections,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.objections = objections;
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.containers = containers;
        this.holds = holds;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task PlaceOwnerAliveAsync(Vault vault, Guid? responsibleUserId, DateTimeOffset reviewAt, CancellationToken ct)
    {
        Hold? existing = await holds.FindActiveOwnerAliveAsync(vault.Id, ct);
        if (existing != null)
        {
            return;
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        Hold hold = new Hold();
        hold.VaultId = vault.Id;
        hold.Scope = HoldScope.Vault;
        hold.Type = HoldType.OwnerAlive;
        hold.Status = HoldStatus.Active;
        hold.Reason = "Owner báo còn sống; chờ Verifier kết luận.";
        hold.ResponsibleUserId = responsibleUserId;
        hold.ReviewAt = reviewAt;
        hold.CreatedAt = now;
        holds.Add(hold);
        vault.Plan.UpdatedAt = now;
        audit.Add(AuditRules.Create(vault.Id, null, AuditActorRole.System, AuditAction.HoldPlaced,
            AuditTargetType.Hold, hold.Id, null, AuditOutcome.Succeeded, "OWNER_ALIVE", now));
    }

    public async Task RescheduleOwnerAliveAsync(Guid vaultId, DateTimeOffset reviewAt, CancellationToken ct)
    {
        Hold? hold = await holds.FindActiveOwnerAliveAsync(vaultId, ct);
        if (hold == null)
        {
            return;
        }
        hold.ReviewAt = reviewAt;
        hold.UpdatedAt = timeProvider.GetUtcNow();
    }

    public async Task ReleaseOwnerAliveAsync(Guid vaultId, Guid? byUserId, string reason, CancellationToken ct)
    {
        Hold? hold = await holds.FindActiveOwnerAliveAsync(vaultId, ct);
        if (hold == null)
        {
            return;
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        hold.Status = HoldStatus.Released;
        hold.ReleasedAt = now;
        hold.ReleasedByUserId = byUserId;
        hold.ReleaseReason = AuditRules.NormalizeReason(reason);
        hold.UpdatedAt = now;
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault != null)
        {
            await ResumeHandoverClockAsync(vault, hold, now, ct);
        }
        audit.Add(AuditRules.Create(vaultId, byUserId, AuditActorRole.Verifier, AuditAction.HoldReleased,
            AuditTargetType.Hold, hold.Id, null, AuditOutcome.Succeeded, "OWNER_ALIVE", now));
    }

    public async Task<PaginatedList<HoldResponse>> ListAsync(
        string? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        HoldStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status.Trim(), HoldStatus.Active.ToApiString(), StringComparison.Ordinal))
            {
                statusFilter = HoldStatus.Active;
            }
            else if (string.Equals(status.Trim(), HoldStatus.Released.ToApiString(), StringComparison.Ordinal))
            {
                statusFilter = HoldStatus.Released;
            }
            else
            {
                throw ValidationErrors.For("status", "Trạng thái chỉ nhận ACTIVE hoặc RELEASED.");
            }
        }
        (List<Hold> items, int total) = await holds.ListAsync(statusFilter, vaultId, pageIndex, pageSize, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        PaginatedList<HoldResponse> page = new PaginatedList<HoldResponse>();
        foreach (Hold hold in items)
        {
            page.Items.Add(OperationsMapper.ToResponse(hold, now));
        }
        page.TotalCount = total;
        page.PageIndex = pageIndex;
        page.PageSize = pageSize;
        page.TotalPages = (total + pageSize - 1) / pageSize;
        page.HasPreviousPage = pageIndex > 1;
        page.HasNextPage = pageIndex < page.TotalPages;
        return page;
    }

    public async Task<HoldResponse> GetAsync(Guid holdId, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        Hold hold = await LoadAsync(holdId, ct);
        return OperationsMapper.ToResponse(hold, timeProvider.GetUtcNow());
    }

    public async Task<HoldResponse> PlaceAsync(PlaceHoldRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string reason = AuditRules.NormalizeReason(request.Reason) ?? string.Empty;
        if (reason.Length == 0)
        {
            throw ValidationErrors.For("reason", "Vui lòng ghi lý do.");
        }

        HoldScope scope = HoldScope.Vault;
        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            if (string.Equals(request.Scope.Trim(), HoldScope.Container.ToApiString(), StringComparison.Ordinal))
            {
                scope = HoldScope.Container;
            }
            else if (!string.Equals(request.Scope.Trim(), HoldScope.Vault.ToApiString(), StringComparison.Ordinal))
            {
                throw ValidationErrors.For("scope", "Phạm vi thủ công chỉ nhận VAULT hoặc CONTAINER.");
            }
        }

        Vault? vault = await vaults.FindByIdAsync(request.VaultId!.Value, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        Guid? targetId = null;
        if (scope == HoldScope.Container)
        {
            if (request.TargetId == null)
            {
                throw ValidationErrors.For("targetId", "Vui lòng chọn gói.");
            }
            Container? container = await containers.FindByIdAsync(vault.Id, request.TargetId.Value, ct);
            if (container == null)
            {
                throw new AppException(AppErrorType.NotFound, ErrorCode.ContainerNotFound, "Không tìm thấy gói.");
            }
            targetId = container.Id;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset reviewAt = now + IncidentRules.MaxReviewInterval;
        if (request.ReviewAt != null)
        {
            if (request.ReviewAt.Value <= now || request.ReviewAt.Value > now + IncidentRules.MaxReviewInterval)
            {
                throw ValidationErrors.For("reviewAt", "Ngày xem lại phải trong vòng 7 ngày tới.");
            }
            reviewAt = request.ReviewAt.Value;
        }

        Hold hold = new Hold();
        hold.VaultId = vault.Id;
        hold.Scope = scope;
        hold.TargetId = targetId;
        hold.Type = HoldType.Admin;
        hold.Status = HoldStatus.Active;
        hold.Reason = reason;
        hold.ResponsibleUserId = adminId;
        hold.ReviewAt = reviewAt;
        hold.CreatedByUserId = adminId;
        hold.CreatedAt = now;
        hold.UpdatedAt = now;
        holds.Add(hold);
        vault.Plan.UpdatedAt = now;
        audit.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.HoldPlaced,
            AuditTargetType.Hold, hold.Id, null, AuditOutcome.Succeeded, reason, now));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToResponse(hold, now);
    }

    public async Task<HoldResponse> ReleaseAsync(Guid holdId, ReleaseHoldRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Hold hold = await LoadAsync(holdId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        // Gọi lặp trên hold đã gỡ: trả trạng thái hiện tại.
        if (hold.Status == HoldStatus.Released)
        {
            return OperationsMapper.ToResponse(hold, now);
        }
        if (hold.Type == HoldType.OwnerAlive)
        {
            // ALIVE-05: chỉ kết luận của Verifier mới gỡ được; Admin không tự gỡ để duyệt thay.
            throw new AppException(AppErrorType.Conflict, ErrorCode.AdminActionNotAllowed,
                "Hold báo còn sống chỉ được gỡ khi Verifier kết luận.",
                new Dictionary<string, object?> { ["reason"] = "HOLD_OWNER_ALIVE" });
        }
        if (!Etag.Matches(request.Etag, hold.UpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Hold vừa được cập nhật ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }
        string reason = AuditRules.NormalizeReason(request.Reason) ?? string.Empty;
        if (reason.Length == 0)
        {
            throw ValidationErrors.For("reason", "Vui lòng ghi lý do gỡ.");
        }

        Vault? vault = await vaults.FindByIdAsync(hold.VaultId, ct);
        hold.Status = HoldStatus.Released;
        hold.ReleasedAt = now;
        hold.ReleasedByUserId = adminId;
        hold.ReleaseReason = reason;
        hold.UpdatedAt = now;
        if (vault != null)
        {
            vault.Plan.UpdatedAt = now;
            await ResumeHandoverClockAsync(vault, hold, now, ct);
        }
        audit.Add(AuditRules.Create(hold.VaultId, adminId, AuditActorRole.Admin, AuditAction.HoldReleased,
            AuditTargetType.Hold, hold.Id, null, AuditOutcome.Succeeded, reason, now));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToResponse(hold, now);
    }

    public async Task<HoldResponse> ReviewAsync(Guid holdId, ReviewHoldRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        Hold hold = await LoadAsync(holdId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (hold.Status != HoldStatus.Active)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.AdminActionNotAllowed, "Hold đã được gỡ.",
                new Dictionary<string, object?> { ["reason"] = "HOLD_RELEASED" });
        }
        if (!Etag.Matches(request.Etag, hold.UpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Hold vừa được cập nhật ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }
        DateTimeOffset next = request.NextReviewAt!.Value;
        if (next <= now || next > now + IncidentRules.MaxReviewInterval)
        {
            throw ValidationErrors.For("nextReviewAt", "Ngày xem lại tiếp theo phải trong vòng 7 ngày tới.");
        }
        hold.ReviewAt = next;
        hold.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToResponse(hold, now);
    }

    /// <summary>
    /// TIME-04: hold cuối cùng của kho vừa được gỡ thì dời các hạn đang chạy của lượt và grant đúng bằng thời gian đã dừng.
    /// Còn hold khác đang hiệu lực thì chưa dời; lần gỡ cuối sẽ dời cho cả đợt dừng liên tục.
    /// </summary>
    private async Task ResumeHandoverClockAsync(Vault vault, Hold released, DateTimeOffset now, CancellationToken ct)
    {
        (List<Hold> vaultHolds, int _) = await holds.ListAsync(null, vault.Id, 1, 100, ct);
        foreach (Hold other in vaultHolds)
        {
            if (other.Id != released.Id && other.Status == HoldStatus.Active)
            {
                return;
            }
        }
        DateTimeOffset pausedFrom = HandoverClockRules.FindPauseStart(released, vaultHolds, now);
        List<HandoverTurn> turns = await objections.FindTurnsWithGrantsAsync(vault.Plan.Id, ct);
        HandoverClockRules.ExtendAfterPause(turns, pausedFrom, now);
    }

    private async Task<Hold> LoadAsync(Guid holdId, CancellationToken ct)
    {
        Hold? hold = await holds.FindByIdAsync(holdId, ct);
        if (hold == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HoldNotFound, "Không tìm thấy hold.");
        }
        return hold;
    }

    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HoldNotFound, "Không tìm thấy hold.");
        }
        return userId;
    }
}
