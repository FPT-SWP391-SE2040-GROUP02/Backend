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
    /// <summary>Admin chỉ xem hold; không có API đặt, xem lại hay gỡ hold bằng tay (hold chỉ do hệ thống đặt và gỡ).</summary>
    Task<PaginatedList<HoldResponse>> ListAsync(string? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>Số hold theo trạng thái cho các thẻ lọc.</summary>
    Task<CountsResponse> CountsAsync(Guid? vaultId, CancellationToken ct);

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
    private readonly IHoldRepository holds;
    private readonly IAuditLogRepository audit;
    private readonly TimeProvider timeProvider;

    public HoldService(
        ICurrentUser currentUser,
        IUserRepository users,
        IHoldRepository holds,
        IAuditLogRepository audit,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.holds = holds;
        this.audit = audit;
        this.timeProvider = timeProvider;
    }

    public async Task<CountsResponse> CountsAsync(Guid? vaultId, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        Dictionary<HoldStatus, int> byStatus = await holds.CountByStatusAsync(vaultId, ct);
        CountsResponse response = new CountsResponse();
        foreach (HoldStatus status in Enum.GetValues<HoldStatus>())
        {
            int count = byStatus.TryGetValue(status, out int found) ? found : 0;
            response.Counts[status.ToApiString()] = count;
            response.Total += count;
        }
        return response;
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
