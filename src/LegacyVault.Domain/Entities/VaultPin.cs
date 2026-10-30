namespace LegacyVault.Domain.Entities;

/// <summary>
/// Mã PIN 6 chữ số của kho (bảng VaultPins). Mỗi kho có tối đa một bản ghi, khóa chính là VaultId.
/// </summary>
/// <remarks>
/// - Chỉ lưu PinHash; không bao giờ lưu hay ghi log PIN gốc (SEC-07).
/// - PIN chặn người dùng tài khoản Owner nhưng không phải Owner thật khỏi các thao tác nhạy cảm
///   (tải nội dung tài sản, xóa tài sản, đổi người nhận/Executor, xóa gói, đổi PIN).
/// - RowVersion là concurrency token: nhiều request đoán PIN song song không đếm sót lần sai.
/// </remarks>
public class VaultPin
{
    public Guid VaultId { get; set; }

    public string PinHash { get; set; } = string.Empty;

    /// <summary>Số lần sai liên tiếp từ lần khóa gần nhất; về 0 khi đúng PIN hoặc khi vừa bị khóa.</summary>
    public int FailedCount { get; set; }

    /// <summary>Bậc khóa lũy tiến (0 = chưa khóa lần nào). Tăng mỗi lần bị khóa, về 0 khi đúng PIN hoặc đặt lại PIN.</summary>
    public int LockLevel { get; set; }

    /// <summary>Thời điểm hết khóa; null hoặc đã qua nghĩa là không bị khóa.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset SetAt { get; set; }

    /// <summary>Lần đổi hoặc đặt lại PIN gần nhất; null khi chưa đổi lần nào.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
