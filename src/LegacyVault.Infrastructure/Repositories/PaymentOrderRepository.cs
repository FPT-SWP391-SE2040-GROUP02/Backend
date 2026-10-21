using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ truy cập dữ liệu theo IPaymentOrderRepository; luật nghiệp vụ nằm ở BillingService/BillingRules.</remarks>
public sealed class PaymentOrderRepository : IPaymentOrderRepository
{
    private readonly AppDbContext context;

    public PaymentOrderRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<PaymentOrder?> FindByIdForVaultAsync(Guid vaultId, Guid orderId, CancellationToken ct)
    {
        return context.PaymentOrders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.VaultId == vaultId, ct);
    }

    public Task<PaymentOrder?> FindByIdAsync(Guid orderId, CancellationToken ct)
    {
        return context.PaymentOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
    }

    public Task<PaymentOrder?> FindByPaymentCodeAsync(string paymentCode, CancellationToken ct)
    {
        return context.PaymentOrders.FirstOrDefaultAsync(o => o.PaymentCode == paymentCode, ct);
    }

    public Task<PaymentOrder?> FindPendingByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return context.PaymentOrders
            .FirstOrDefaultAsync(o => o.VaultId == vaultId && o.Status == PaymentOrderStatus.Pending, ct);
    }

    public async Task<bool> TryAddAsync(PaymentOrder order, CancellationToken ct)
    {
        context.PaymentOrders.Add(order);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (DbExceptions.IsUniqueViolation(exception))
        {
            DbExceptions.DetachAll(context, order);
            return false;
        }
    }

    /// <remarks>
    /// ChangeTracker.Clear bỏ cả kho và quyền dịch vụ đã nạp; người gọi chỉ đọc lại đơn sau đó,
    /// không dùng lại các entity cũ.
    /// </remarks>
    public void DiscardPendingChanges()
    {
        context.ChangeTracker.Clear();
    }
}
