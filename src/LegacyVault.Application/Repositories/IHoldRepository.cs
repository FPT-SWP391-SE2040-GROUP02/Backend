using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Holds. Hàm Find trả entity có theo dõi thay đổi; hàm Has chỉ đọc.</summary>
public interface IHoldRepository
{
    void Add(Hold hold);

    Task<Hold?> FindByIdAsync(Guid holdId, CancellationToken ct);

    /// <summary>Hold F2C đang hiệu lực của kho (nếu có).</summary>
    Task<Hold?> FindActiveOwnerAliveAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Kho có hold nào đang hiệu lực không (mọi phạm vi) — chặn phát hành, kích hoạt và dọn dữ liệu.</summary>
    Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct);

    Task<(List<Hold> Items, int TotalCount)> ListAsync(
        HoldStatus? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct);

    Task<int> CountActiveAsync(CancellationToken ct);

    /// <summary>Hold đang hiệu lực đã quá ngày xem lại (chưa ai xem lại), dùng cho dashboard và job nhắc.</summary>
    Task<int> CountReviewOverdueAsync(DateTimeOffset now, CancellationToken ct);

    bool IsUniqueViolation(Exception exception);
}
