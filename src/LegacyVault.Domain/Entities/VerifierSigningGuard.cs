namespace LegacyVault.Domain.Entities;

/// <summary>
/// DEATH-03: bộ đếm nhập sai mật khẩu xác nhận duyệt/từ chối của một Verifier (bảng VerifierSigningGuards), cấu trúc theo VaultPin.
/// </summary>
/// <remarks>
/// - Khóa chính là UserId: mỗi Verifier một dòng, chỉ tạo khi nhập sai lần đầu.
/// - Sai 5 lần liên tiếp thì khóa chức năng duyệt/từ chối 15 phút, 1 giờ, rồi 24 giờ (dùng VaultPinRules).
///   Chỉ khóa duyệt/từ chối, không khóa đăng nhập hay xem hồ sơ.
/// - RowVersion là concurrency token: nhiều request đoán mật khẩu song song không đếm sót lần sai.
/// </remarks>
public class VerifierSigningGuard
{
    public Guid UserId { get; set; }

    /// <summary>Số lần sai liên tiếp từ lần khóa gần nhất; về 0 khi đúng mật khẩu hoặc khi vừa bị khóa.</summary>
    public int FailedCount { get; set; }

    /// <summary>Bậc khóa lũy tiến (0 = chưa khóa lần nào). Tăng mỗi lần bị khóa, về 0 khi đúng mật khẩu.</summary>
    public int LockLevel { get; set; }

    /// <summary>Thời điểm hết khóa; null hoặc đã qua nghĩa là không bị khóa.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
