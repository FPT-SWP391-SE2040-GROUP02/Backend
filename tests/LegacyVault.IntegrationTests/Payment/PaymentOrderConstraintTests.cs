using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// PAY-04: ràng buộc DB của bảng PaymentOrders trên SQL Server thật (database tạm, xem DeathCaseRepositoryTests).
/// </summary>
public sealed class PaymentOrderConstraintTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    /// <summary>Tạo user và kho qua API, trả (vaultId, ownerId).</summary>
    private async Task<(Guid VaultId, Guid OwnerId)> CreateVaultAsync()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid ownerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            ownerId = vault.OwnerId;
        });
        return (vaultId, ownerId);
    }

    private static PaymentOrder NewOrder(Guid vaultId, Guid ownerId, PaymentOrderStatus status, string paymentCode)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PaymentOrder order = new PaymentOrder();
        order.VaultId = vaultId;
        order.OwnerId = ownerId;
        order.Kind = PaymentOrderKind.Purchase;
        order.Tier = ServiceTier.Xs;
        order.Years = 1;
        order.ExtraSlots = 0;
        order.TierAmount = 199000;
        order.SlotAmount = 0;
        order.TotalAmount = 199000;
        order.StorageQuotaBytes = 200 * 1_048_576L;
        order.IncludedBeneficiarySlots = 2;
        order.PolicyVersion = "2026-10";
        order.PaymentCode = paymentCode;
        order.Status = status;
        order.ExpiresAt = now.AddMinutes(15);
        order.CreatedAt = now;
        order.UpdatedAt = now;
        return order;
    }

    /// <summary>Thêm một đơn rồi lưu; trả false nếu DB báo vi phạm unique, lỗi khác thì ném lại.</summary>
    private async Task<bool> TryAddAsync(PaymentOrder order)
    {
        bool added = false;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.PaymentOrders.Add(order);
            try
            {
                await db.SaveChangesAsync();
                added = true;
            }
            catch (DbUpdateException exception)
            {
                if (!DbExceptions.IsUniqueViolation(exception))
                {
                    throw;
                }
            }
        });
        return added;
    }

    private async Task<int> CountOrdersAsync(Guid vaultId, PaymentOrderStatus status)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentOrders.CountAsync(o => o.VaultId == vaultId && o.Status == status);
        });
        return count;
    }

    [SqlFact]
    public async Task Pay04_DatabaseRejectsSecondPendingOrderForVault()
    {
        // Chuẩn bị
        (Guid vaultId, Guid ownerId) = await CreateVaultAsync();
        bool firstAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVAAAAAA22"));

        // Hành động
        bool secondAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVBBBBBB22"));

        // Kiểm tra
        Assert.True(firstAdded);
        Assert.False(secondAdded);
        Assert.Equal(1, await CountOrdersAsync(vaultId, PaymentOrderStatus.Pending));
    }

    [SqlFact]
    public async Task Pay04_PendingAndExpiredOrdersOfSameVaultCanCoexist()
    {
        // Chuẩn bị
        (Guid vaultId, Guid ownerId) = await CreateVaultAsync();
        bool expiredAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Expired, "LVAAAAAA22"));
        bool cancelledAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Cancelled, "LVAAAAAA23"));

        // Hành động
        bool pendingAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVBBBBBB22"));

        // Kiểm tra: index lọc chỉ tính đơn Pending
        Assert.True(expiredAdded);
        Assert.True(cancelledAdded);
        Assert.True(pendingAdded);
        Assert.Equal(1, await CountOrdersAsync(vaultId, PaymentOrderStatus.Pending));
        Assert.Equal(1, await CountOrdersAsync(vaultId, PaymentOrderStatus.Expired));
    }

    [SqlFact]
    public async Task Pay04_DifferentVaultsCanEachHavePendingOrder()
    {
        // Chuẩn bị
        (Guid vaultId, Guid ownerId) = await CreateVaultAsync();
        (Guid otherVaultId, Guid otherOwnerId) = await CreateVaultAsync();

        // Hành động
        bool firstAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVAAAAAA22"));
        bool secondAdded = await TryAddAsync(NewOrder(otherVaultId, otherOwnerId, PaymentOrderStatus.Pending, "LVBBBBBB22"));

        // Kiểm tra
        Assert.True(firstAdded);
        Assert.True(secondAdded);
    }

    [SqlFact]
    public async Task Pay04_DatabaseRejectsDuplicatePaymentCode()
    {
        // Chuẩn bị
        (Guid vaultId, Guid ownerId) = await CreateVaultAsync();
        (Guid otherVaultId, Guid otherOwnerId) = await CreateVaultAsync();
        bool firstAdded = await TryAddAsync(NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVAAAAAA22"));

        // Hành động: kho khác nhưng trùng mã chuyển khoản
        bool secondAdded = await TryAddAsync(NewOrder(otherVaultId, otherOwnerId, PaymentOrderStatus.Pending, "LVAAAAAA22"));

        // Kiểm tra
        Assert.True(firstAdded);
        Assert.False(secondAdded);
    }

    [SqlFact]
    public async Task Pay04_ExpireOldPendingAndInsertNewPendingInOneSave_Succeeds()
    {
        // Chuẩn bị: đơn cũ Pending đã lưu trong DB
        (Guid vaultId, Guid ownerId) = await CreateVaultAsync();
        PaymentOrder oldOrder = NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVAAAAAA22");
        await TryAddAsync(oldOrder);

        // Hành động: một lần SaveChanges vừa đổi đơn cũ sang Expired vừa thêm đơn Pending mới (kịch bản D7 ở bước tạo đơn)
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            PaymentOrder stored = await db.PaymentOrders.SingleAsync(o => o.Id == oldOrder.Id);
            stored.Status = PaymentOrderStatus.Expired;
            stored.UpdatedAt = DateTimeOffset.UtcNow;
            db.PaymentOrders.Add(NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVBBBBBB22"));
            await db.SaveChangesAsync();
        });

        // Kiểm tra
        Assert.Equal(1, await CountOrdersAsync(vaultId, PaymentOrderStatus.Pending));
        Assert.Equal(1, await CountOrdersAsync(vaultId, PaymentOrderStatus.Expired));
    }

    [SqlFact]
    public async Task Pay04_RowVersionRejectsSecondConcurrentUpdate()
    {
        // Chuẩn bị
        (Guid vaultId, Guid ownerId) = await CreateVaultAsync();
        PaymentOrder order = NewOrder(vaultId, ownerId, PaymentOrderStatus.Pending, "LVAAAAAA22");
        await TryAddAsync(order);

        // Hành động: hai context cùng đọc đơn, bên đầu lưu trước, bên sau lưu với RowVersion cũ
        bool secondFailed = false;
        await ApiTestHelpers.WithDbAsync(factory, async firstDb =>
        {
            await ApiTestHelpers.WithDbAsync(factory, async secondDb =>
            {
                PaymentOrder first = await firstDb.PaymentOrders.SingleAsync(o => o.Id == order.Id);
                PaymentOrder second = await secondDb.PaymentOrders.SingleAsync(o => o.Id == order.Id);

                first.Status = PaymentOrderStatus.Paid;
                first.PaidAt = DateTimeOffset.UtcNow;
                await firstDb.SaveChangesAsync();

                second.Status = PaymentOrderStatus.Cancelled;
                second.CancelledAt = DateTimeOffset.UtcNow;
                try
                {
                    await secondDb.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    secondFailed = true;
                }
            });
        });

        // Kiểm tra
        Assert.True(secondFailed);
        Assert.Equal(1, await CountOrdersAsync(vaultId, PaymentOrderStatus.Paid));
    }
}
