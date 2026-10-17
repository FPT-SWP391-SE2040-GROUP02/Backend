using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Kết quả phụ của TryReserveAsync: phần còn lại khi không đủ chỗ.</summary>
public sealed class ReservationOutcome
{
    public long RemainingBytes { get; set; }
}

/// <summary>Giữ chỗ dung lượng cho lần tải lên (QUOTA-02).</summary>
public interface IStorageReservationRepository
{
    /// <summary>
    /// Kiểm dung lượng và ghi chỗ giữ trong MỘT bước dưới khóa riêng của kho, nên hai lần tải đồng thời không cùng
    /// qua bước kiểm. Đủ chỗ thì ghi chỗ giữ (đã lưu xuống DB) và trả về; không đủ thì trả null và ghi phần còn lại vào outcome.
    /// </summary>
    Task<StorageReservation?> TryReserveAsync(
        Guid vaultId, long bytes, long quotaBytes, DateTimeOffset now, TimeSpan holdFor,
        ReservationOutcome outcome, CancellationToken ct);

    /// <summary>Đánh dấu chỗ giữ đã dùng (cùng lần lưu với tài sản). Chưa lưu.</summary>
    void MarkCommitted(StorageReservation reservation, DateTimeOffset now);

    /// <summary>Trả chỗ giữ khi tải lên lỗi, lưu ngay. Lỗi khi trả chỗ không ném ra (chỗ giữ tự hết hạn sau 15 phút).</summary>
    Task ReleaseAsync(Guid reservationId, DateTimeOffset now);
}
