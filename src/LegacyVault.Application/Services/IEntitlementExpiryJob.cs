namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền của hết hạn dịch vụ: kho trả phí hết kỳ thì sang ân hạn chỉ đọc (SRS OPLAN-01).
/// </summary>
/// <remarks>
/// Hai hàm tách nhau để EntitlementExpiryMonitor mở một DI scope (một DbContext) riêng cho mỗi kho:
/// một kho lỗi không làm hỏng kho khác. Trạng thái nằm trong DB nên chạy lặp không ghi trùng.
/// </remarks>
public interface IEntitlementExpiryJob
{
    /// <summary>Id các kho PAID_ACTIVE đã tới hạn dịch vụ lúc này.</summary>
    Task<IReadOnlyList<Guid>> FindExpiredVaultIdsAsync(CancellationToken ct);

    /// <summary>
    /// Xử lý một kho: kiểm lại điều kiện trong transaction Serializable (cùng mức với cấp quyền thanh toán) rồi chuyển sang
    /// GRACE_READ_ONLY và ghi nhật ký. Trả true nếu đã chuyển; false nếu kho không còn đủ điều kiện (vừa gia hạn, đã chuyển).
    /// </summary>
    Task<bool> ProcessVaultAsync(Guid vaultId, CancellationToken ct);
}
