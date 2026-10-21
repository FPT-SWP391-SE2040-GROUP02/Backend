using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng PaymentOrders.</summary>
public interface IPaymentOrderRepository
{
    /// <summary>
    /// Đơn theo Id nhưng chỉ khi thuộc đúng kho (có theo dõi thay đổi). Đơn của kho khác hoặc không có → null,
    /// để service trả 404 mà không lộ sự tồn tại của đơn (SEC-04).
    /// </summary>
    Task<PaymentOrder?> FindByIdForVaultAsync(Guid vaultId, Guid orderId, CancellationToken ct);

    /// <summary>
    /// Đơn theo Id, không giới hạn kho (có theo dõi thay đổi). Chỉ dùng ở luồng ghi nhận thanh toán không có
    /// người dùng đăng nhập (webhook H2); luồng của Owner dùng FindByIdForVaultAsync. Không có → null.
    /// </summary>
    Task<PaymentOrder?> FindByIdAsync(Guid orderId, CancellationToken ct);

    /// <summary>
    /// Đơn theo mã thanh toán (nội dung chuyển khoản), có theo dõi thay đổi. Dùng ở luồng webhook SePay
    /// để đối chiếu giao dịch với đơn (PAY-02). Không có → null.
    /// </summary>
    Task<PaymentOrder?> FindByPaymentCodeAsync(string paymentCode, CancellationToken ct);

    /// <summary>Đơn đang Pending của kho (PAY-04: tối đa một), có theo dõi thay đổi. Không có → null.</summary>
    Task<PaymentOrder?> FindPendingByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Thêm đơn rồi lưu NGAY, kèm mọi thay đổi đang chờ trong cùng AppDbContext (đổi đơn cũ sang Expired, dòng nhật ký),
    /// nên tất cả thành công hoặc cùng không được lưu. Trả false khi DB từ chối vì vi phạm unique:
    /// mỗi kho một đơn Pending (request khác vừa tạo trước) hoặc trùng PaymentCode. Khi đó người gọi nên
    /// DiscardPendingChanges rồi đọc lại.
    /// </summary>
    Task<bool> TryAddAsync(PaymentOrder order, CancellationToken ct);

    /// <summary>Bỏ mọi thay đổi chưa lưu của request (sau khi TryAddAsync trả false).</summary>
    void DiscardPendingChanges();
}
