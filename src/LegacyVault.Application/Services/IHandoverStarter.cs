namespace LegacyVault.Application.Services;

/// <summary>
/// Mở giai đoạn nhận tài sản cho một kho: tạo mỗi chỉ định một lượt, ghim danh sách tài sản và gửi lời mời.
/// Được DeathCaseReviewService gọi ngay khi Người xét duyệt duyệt hồ sơ chứng tử, trong cùng một lần lưu.
/// </summary>
public interface IHandoverStarter
{
    /// <summary>Mở bàn giao cho kho. Gọi lặp thì trả kết quả cũ (AlreadyStarted), không tạo lượt hay gửi thư lần nữa.</summary>
    Task<HandoverStartResult> StartAsync(Guid vaultId, CancellationToken ct);
}
