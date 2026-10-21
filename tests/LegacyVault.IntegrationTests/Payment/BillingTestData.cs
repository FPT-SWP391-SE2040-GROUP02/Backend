using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Dữ liệu thử dùng chung cho test thanh toán. Toàn bộ là dữ liệu giả sinh trong test, không có dữ liệu thật.</summary>
public static class BillingTestData
{
    /// <summary>User mới có kho Free, kèm eKYC Owner đạt cách đây ekycDaysAgo ngày (null = chưa có eKYC).</summary>
    public static async Task<(HttpClient Client, Guid VaultId, Guid OwnerId)> CreateFreeVaultAsync(
        ApiFactory factory, int? ekycDaysAgo)
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid ownerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            ownerId = vault.OwnerId;
            if (ekycDaysAgo != null)
            {
                EkycVerification verification = new EkycVerification();
                verification.UserId = ownerId;
                verification.Purpose = EkycPurpose.OwnerEnrollment;
                verification.Status = EkycStatus.Passed;
                verification.CreatedAt = DateTimeOffset.UtcNow.AddDays(-ekycDaysAgo.Value);
                verification.CompletedAt = DateTimeOffset.UtcNow.AddDays(-ekycDaysAgo.Value);
                db.EkycVerifications.Add(verification);
                await db.SaveChangesAsync();
            }
        });
        return (client, vaultId, ownerId);
    }

    /// <summary>Đặt kho thành XS đang trả phí, hết hạn sau serviceLeft (sửa thẳng DB, chưa có dev-pay).</summary>
    public static async Task MakePaidActiveAsync(ApiFactory factory, Guid vaultId, TimeSpan serviceLeft)
    {
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset expiresAt = DateTimeOffset.UtcNow + serviceLeft;
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, expiresAt));
        });
    }

    /// <summary>Đổi trạng thái một đơn bằng cách sửa thẳng DB (mô phỏng đơn đã trả, đã hủy...).</summary>
    public static async Task SetOrderStatusAsync(ApiFactory factory, Guid orderId, PaymentOrderStatus status)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.PaymentOrders.Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));
        });
    }

    /// <summary>Làm đơn quá hạn thanh toán 1 phút bằng cách lùi ExpiresAt (D7: không có job hết hạn).</summary>
    public static async Task MakeOrderOverdueAsync(ApiFactory factory, Guid orderId)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset overdue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.PaymentOrders.Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, overdue));
        });
    }

    public static async Task<PaymentOrder> LoadOrderAsync(ApiFactory factory, Guid orderId)
    {
        PaymentOrder? order = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            order = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        });
        return order!;
    }

    public static async Task<int> CountOrdersAsync(ApiFactory factory, Guid vaultId, PaymentOrderStatus? status)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            IQueryable<PaymentOrder> query = db.PaymentOrders.Where(o => o.VaultId == vaultId);
            if (status != null)
            {
                query = query.Where(o => o.Status == status.Value);
            }
            count = await query.CountAsync();
        });
        return count;
    }
}
