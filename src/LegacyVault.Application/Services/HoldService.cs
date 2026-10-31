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

    /// <summary>Admin chỉ xem hold; không có API đặt, xem lại hay gỡ hold bằng tay (hold chỉ do hệ thống đặt và gỡ).</summary>
    Task<PaginatedList<HoldResponse>> ListAsync(string? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct);

    Task<HoldResponse> GetAsync(Guid holdId, CancellationToken ct);
}

/// <remarks>
/// - Hold luôn có lý do, người phụ trách và ngày xem lại tối đa 7 ngày (SRS §6.4).
/// - Đặt hoặc gỡ ghi plan.UpdatedAt trong cùng giao dịch: nhận lượt, đồng ý, duyệt chạy cùng lúc thua RowVersion của kế hoạch.
/// - Không có thao tác nào ở đây đọc nội dung tài sản; Admin không mở kho (ASSIGN-05).
/// - Admin chỉ xem danh sách và chi tiết hold; chức năng Admin đặt/xem lại/gỡ hold bằng tay đã bỏ theo quyết định của nhóm.
/// - Gỡ hold (hết hold của kho) thì dời hạn phản hồi, hạn quyết định và hạn grant đúng bằng thời gian đã dừng (TIME-04, H1).
/// </remarks>
public sealed class HoldService : IHoldService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IHoldRepository holds;
    private readonly IObjectionRepository objections;
    private readonly IAuditLogRepository audit;
    private readonly TimeProvider timeProvider;

    public HoldService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IHoldRepository holds,
        IObjectionRepository objections,
        IAuditLogRepository audit,
        TimeProvider timeProvider)
    {
        this.objections = objections;
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.holds = holds;
        this.audit = audit;
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
