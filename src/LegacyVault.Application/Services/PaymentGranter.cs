using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật cấp quyền dịch vụ khi một đơn được trả tiền (SRS §8.3 bước 7–9), dùng chung cho dev-pay và webhook SePay.
/// </summary>
/// <remarks>
/// Lớp này KHÔNG gọi SaveChanges và KHÔNG mở transaction: người gọi bọc ExecuteSerializableAsync và tự lưu một lần
/// (VaultEntitlement không có concurrency token, hai đơn khác nhau cùng ghi một quyền dịch vụ phải tuần tự).
/// SEC-07: nhật ký không chứa số tiền hay mã thanh toán.
/// </remarks>
public sealed class PaymentGranter
{
    private readonly IVaultRepository vaultRepository;
    private readonly IAuditLogRepository auditLogRepository;

    public PaymentGranter(IVaultRepository vaultRepository, IAuditLogRepository auditLogRepository)
    {
        this.vaultRepository = vaultRepository;
        this.auditLogRepository = auditLogRepository;
    }

    /// <summary>
    /// Thứ tự: đơn đã trả → đơn đã hủy / quá hạn → kho đóng → cấp quyền → đổi đơn → nhật ký.
    /// Trả true khi vừa cấp quyền (đơn đã chuyển Paid, người gọi phải lưu); false khi đơn đã Paid từ trước
    /// (PAY-04: không cấp lần hai, không có gì để lưu). Không đủ điều kiện thì ném AppException PAYMENT_ORDER_NOT_PAYABLE.
    /// </summary>
    public async Task<bool> GrantAsync(PaymentOrder order, DateTimeOffset paidAt, CancellationToken ct)
    {
        // PAY-04: một đơn chỉ cấp quyền một lần.
        if (order.Status == PaymentOrderStatus.Paid)
        {
            return false;
        }
        if (order.Status == PaymentOrderStatus.Cancelled)
        {
            throw BillingRules.NotPayable(PaymentOrderNotPayableReason.Cancelled, "Đơn đã bị hủy nên không thể thanh toán.");
        }
        // PAY-06: so với giờ tiền vào, không phải trạng thái Expired do D7 đánh dấu lúc đọc.
        if (paidAt >= order.ExpiresAt)
        {
            throw BillingRules.NotPayable(PaymentOrderNotPayableReason.Expired, "Đơn đã hết hạn thanh toán.");
        }

        Vault? vault = await vaultRepository.FindByIdAsync(order.VaultId, ct);
        if (vault == null)
        {
            throw BillingRules.OrderNotFound();
        }
        // AC-104: kho đã đóng thì không cấp quyền, không khôi phục.
        if (vault.Status == VaultStatus.Closed)
        {
            throw BillingRules.NotPayable(PaymentOrderNotPayableReason.VaultClosed, "Kho đã đóng nên không thể cấp dịch vụ.");
        }

        // §8.3: không kiểm trạng thái kế hoạch lúc trả; đơn tạo trước vẫn được cấp. SETUP-14: không đổi kế hoạch.
        if (order.Kind == PaymentOrderKind.Purchase)
        {
            BillingRules.ApplyPurchase(vault.Entitlement, order, paidAt);
        }
        else if (order.Kind == PaymentOrderKind.Upgrade)
        {
            BillingRules.ApplyUpgrade(vault.Entitlement, order, paidAt);
        }
        else
        {
            BillingRules.ApplyAddon(vault.Entitlement, order, paidAt);
        }

        order.Status = PaymentOrderStatus.Paid;
        order.PaidAt = paidAt;
        order.UpdatedAt = paidAt;

        // Nhật ký của hệ thống (không có người dùng) vì webhook cũng đi đường này.
        auditLogRepository.Add(AuditRules.Create(
            vault.Id, null, AuditActorRole.System, AuditAction.PaymentPaid, AuditTargetType.PaymentOrder,
            order.Id, null, AuditOutcome.Succeeded, null, paidAt));
        return true;
    }
}
