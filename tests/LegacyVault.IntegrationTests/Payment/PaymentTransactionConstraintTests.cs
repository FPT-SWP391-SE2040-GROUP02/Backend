using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// PAY-04, PAY-05, AC-29: ràng buộc DB của bảng PaymentTransactions trên SQL Server thật (database tạm, xem PaymentOrderConstraintTests).
/// </summary>
public sealed class PaymentTransactionConstraintTests : IAsyncLifetime
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

    /// <summary>Tạo user, kho và một đơn Paid trực tiếp trong DB, trả Id đơn (để giao dịch có FK hợp lệ).</summary>
    private async Task<Guid> CreateOrderAsync(string paymentCode)
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid orderId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PaymentOrder order = new PaymentOrder();
            order.VaultId = vaultId;
            order.OwnerId = vault.OwnerId;
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
            order.Status = PaymentOrderStatus.Expired;
            order.ExpiresAt = now.AddMinutes(15);
            order.CreatedAt = now;
            order.UpdatedAt = now;
            db.PaymentOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        });
        return orderId;
    }

    private static PaymentTransaction NewTransaction(
        string provider,
        long providerTransactionId,
        PaymentTransactionStatus status,
        Guid? paymentOrderId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PaymentTransaction transaction = new PaymentTransaction();
        transaction.Provider = provider;
        transaction.Environment = "Test";
        transaction.ProviderTransactionId = providerTransactionId;
        transaction.AccountNumber = "0000000000";
        transaction.TransferType = "in";
        transaction.TransferAmount = 199000;
        transaction.ReceivedAt = now;
        transaction.Status = status;
        transaction.PaymentOrderId = paymentOrderId;
        return transaction;
    }

    /// <summary>Thêm một giao dịch rồi lưu; trả false nếu DB báo vi phạm unique, lỗi khác thì ném lại.</summary>
    private async Task<bool> TryAddAsync(PaymentTransaction transaction)
    {
        bool added = false;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.PaymentTransactions.Add(transaction);
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

    private async Task<int> CountTransactionsAsync(Guid paymentOrderId, PaymentTransactionStatus status)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentTransactions.CountAsync(t => t.PaymentOrderId == paymentOrderId && t.Status == status);
        });
        return count;
    }

    [SqlFact]
    public async Task Pay05_Ac29_DatabaseRejectsDuplicateProviderTransactionId()
    {
        // Chuẩn bị
        bool firstAdded = await TryAddAsync(NewTransaction("SePay", 1001, PaymentTransactionStatus.Received, null));

        // Hành động: webhook gửi lặp cùng ID giao dịch SePay
        bool secondAdded = await TryAddAsync(NewTransaction("SePay", 1001, PaymentTransactionStatus.Received, null));

        // Kiểm tra
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentTransactions.CountAsync(t => t.ProviderTransactionId == 1001);
        });
        Assert.True(firstAdded);
        Assert.False(secondAdded);
        Assert.Equal(1, count);
    }

    [SqlFact]
    public async Task Pay05_SameTransactionIdWithDifferentProviderIsAllowed()
    {
        // Chuẩn bị
        bool firstAdded = await TryAddAsync(NewTransaction("SePay", 1002, PaymentTransactionStatus.Received, null));

        // Hành động: khóa chống trùng gồm cả Provider, nên nhà cung cấp khác dùng lại số ID vẫn được
        bool secondAdded = await TryAddAsync(NewTransaction("OtherProvider", 1002, PaymentTransactionStatus.Received, null));

        // Kiểm tra
        Assert.True(firstAdded);
        Assert.True(secondAdded);
    }

    [SqlFact]
    public async Task Pay04_DatabaseRejectsTwoMatchedTransactionsForSameOrder()
    {
        // Chuẩn bị
        Guid orderId = await CreateOrderAsync("LVAAAAAA22");
        bool firstAdded = await TryAddAsync(NewTransaction("SePay", 2001, PaymentTransactionStatus.Matched, orderId));

        // Hành động: giao dịch thứ hai (ID SePay khác) cũng đòi Matched cho cùng đơn
        bool secondAdded = await TryAddAsync(NewTransaction("SePay", 2002, PaymentTransactionStatus.Matched, orderId));

        // Kiểm tra
        Assert.True(firstAdded);
        Assert.False(secondAdded);
        Assert.Equal(1, await CountTransactionsAsync(orderId, PaymentTransactionStatus.Matched));
    }

    [SqlFact]
    public async Task Pay04_MatchedAndReconciliationRequiredOfSameOrderCanCoexist()
    {
        // Chuẩn bị
        Guid orderId = await CreateOrderAsync("LVAAAAAA22");
        bool matchedAdded = await TryAddAsync(NewTransaction("SePay", 3001, PaymentTransactionStatus.Matched, orderId));

        // Hành động: giao dịch trả thừa của cùng đơn bị đánh dấu cần đối chiếu (AC-97), vẫn phải lưu được
        bool firstReconciliationAdded = await TryAddAsync(NewTransaction("SePay", 3002, PaymentTransactionStatus.ReconciliationRequired, orderId));
        bool secondReconciliationAdded = await TryAddAsync(NewTransaction("SePay", 3003, PaymentTransactionStatus.ReconciliationRequired, orderId));

        // Kiểm tra: index lọc chỉ tính giao dịch Matched
        Assert.True(matchedAdded);
        Assert.True(firstReconciliationAdded);
        Assert.True(secondReconciliationAdded);
        Assert.Equal(1, await CountTransactionsAsync(orderId, PaymentTransactionStatus.Matched));
        Assert.Equal(2, await CountTransactionsAsync(orderId, PaymentTransactionStatus.ReconciliationRequired));
    }

    [SqlFact]
    public async Task Pay04_MatchedTransactionsOfDifferentOrdersAndReceivedWithoutOrderAreAllowed()
    {
        // Chuẩn bị
        Guid orderId = await CreateOrderAsync("LVAAAAAA22");
        Guid otherOrderId = await CreateOrderAsync("LVBBBBBB22");

        // Hành động: hai đơn khác nhau mỗi đơn một Matched; nhiều giao dịch Received chưa gắn đơn (PaymentOrderId null)
        bool firstMatchedAdded = await TryAddAsync(NewTransaction("SePay", 4001, PaymentTransactionStatus.Matched, orderId));
        bool secondMatchedAdded = await TryAddAsync(NewTransaction("SePay", 4002, PaymentTransactionStatus.Matched, otherOrderId));
        bool firstReceivedAdded = await TryAddAsync(NewTransaction("SePay", 4003, PaymentTransactionStatus.Received, null));
        bool secondReceivedAdded = await TryAddAsync(NewTransaction("SePay", 4004, PaymentTransactionStatus.Received, null));

        // Kiểm tra
        Assert.True(firstMatchedAdded);
        Assert.True(secondMatchedAdded);
        Assert.True(firstReceivedAdded);
        Assert.True(secondReceivedAdded);
    }

    [SqlFact]
    public async Task Pay05_RowVersionRejectsSecondConcurrentUpdate()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction("SePay", 5001, PaymentTransactionStatus.Received, null);
        await TryAddAsync(transaction);

        // Hành động: hai context cùng đọc giao dịch, bên đầu lưu trước, bên sau lưu với RowVersion cũ
        bool secondFailed = false;
        await ApiTestHelpers.WithDbAsync(factory, async firstDb =>
        {
            await ApiTestHelpers.WithDbAsync(factory, async secondDb =>
            {
                PaymentTransaction first = await firstDb.PaymentTransactions.SingleAsync(t => t.Id == transaction.Id);
                PaymentTransaction second = await secondDb.PaymentTransactions.SingleAsync(t => t.Id == transaction.Id);

                first.Status = PaymentTransactionStatus.ReconciliationRequired;
                first.ReconciliationReason = "AMOUNT_MISMATCH";
                await firstDb.SaveChangesAsync();

                second.Status = PaymentTransactionStatus.ReconciliationRequired;
                second.ReconciliationReason = "WRONG_ACCOUNT";
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
        string? storedReason = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            PaymentTransaction stored = await db.PaymentTransactions.SingleAsync(t => t.Id == transaction.Id);
            storedReason = stored.ReconciliationReason;
        });
        Assert.True(secondFailed);
        Assert.Equal("AMOUNT_MISMATCH", storedReason);
    }
}
