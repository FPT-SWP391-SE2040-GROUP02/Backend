using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Chỗ giữ dung lượng của một lần tải lên (bảng StorageReservations), QUOTA-02, ASSET-04.
/// </summary>
/// <remarks>
/// - Kiểm dung lượng tính cả phần các lần tải khác đang giữ chỗ: đã dùng + giữ chỗ còn hạn + lần này không quá hạn mức.
/// - Việc kiểm và ghi chỗ giữ chạy dưới khóa riêng của kho (sp_getapplock) nên hai lần tải đồng thời không cùng qua bước kiểm.
/// - Giữ 15 phút; quá hạn thì không còn tính nên lần tải bị treo không chiếm dung lượng mãi.
/// </remarks>
public class StorageReservation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public long Bytes { get; set; }

    public StorageReservationStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}
