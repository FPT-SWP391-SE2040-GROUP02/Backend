using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Thứ tự đối chiếu giao dịch SePay với đơn (SRS §8.3 bước 6–8, PAY-03..07, AC-28, AC-30). Thuần logic, không cần database.</summary>
public sealed class SePayReconciliationRulesTests
{
    private const string BankAccount = "0000000000";
    private const string PaymentCode = "LVAAAAAA22";

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static PaymentTransaction NewTransaction()
    {
        PaymentTransaction transaction = new PaymentTransaction();
        transaction.Provider = "SePay";
        transaction.Environment = "Test";
        transaction.ProviderTransactionId = 1;
        transaction.AccountNumber = BankAccount;
        transaction.MatchedPaymentCode = PaymentCode;
        transaction.TransferType = "in";
        transaction.TransferAmount = 199000;
        transaction.TransactionAt = Now;
        transaction.ReceivedAt = Now;
        transaction.Status = PaymentTransactionStatus.Received;
        return transaction;
    }

    private static PaymentOrder NewOrder(PaymentOrderStatus status)
    {
        PaymentOrder order = new PaymentOrder();
        order.PaymentCode = PaymentCode;
        order.TotalAmount = 199000;
        order.Status = status;
        order.ExpiresAt = Now.AddMinutes(10);
        return order;
    }

    [Fact]
    public void Pay07_ValidIncomingTransaction_HasNoReason()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Null(reason);
    }

    [Fact]
    public void Pay07_OutgoingTransferType_IsNotIncomingEvenWhenEverythingElseIsWrong()
    {
        // Chuẩn bị: tiền ra, lại sai tài khoản và không có đơn: lý do đầu tiên trong thứ tự là NOT_INCOMING
        PaymentTransaction transaction = NewTransaction();
        transaction.TransferType = "out";
        transaction.AccountNumber = "9999999999";

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, null);

        // Kiểm tra
        Assert.Equal("NOT_INCOMING", reason);
    }

    [Fact]
    public void Pay03_DifferentAccountNumber_IsWrongAccount()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        transaction.AccountNumber = "9999999999";
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Equal("WRONG_ACCOUNT", reason);
    }

    [Fact]
    public void Pay03_BankAccountNotConfigured_EveryTransactionIsWrongAccount()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);

        // Hành động
        string? emptyConfigured = SePayRules.FindReconciliationReason(transaction, "", order);
        string? blankConfigured = SePayRules.FindReconciliationReason(transaction, "  ", order);

        // Kiểm tra
        Assert.Equal("WRONG_ACCOUNT", emptyConfigured);
        Assert.Equal("WRONG_ACCOUNT", blankConfigured);
    }

    [Fact]
    public void Pay03_AccountNumberWithSpaces_StillMatches()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        transaction.AccountNumber = "0000000000";
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, " 0000000000 ", order);

        // Kiểm tra
        Assert.Null(reason);
    }

    [Fact]
    public void Pay02_NoPaymentCode_IsCodeMissing()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        transaction.MatchedPaymentCode = null;

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, null);

        // Kiểm tra
        Assert.Equal("CODE_MISSING", reason);
    }

    [Fact]
    public void Pay02_CodeWithoutOrder_IsOrderNotFound()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, null);

        // Kiểm tra
        Assert.Equal("ORDER_NOT_FOUND", reason);
    }

    [Fact]
    public void Ac28_UnderpaidAndOverpaid_AreAmountMismatch()
    {
        // Chuẩn bị
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);
        PaymentTransaction underpaid = NewTransaction();
        underpaid.TransferAmount = 198999;
        PaymentTransaction overpaid = NewTransaction();
        overpaid.TransferAmount = 199001;

        // Hành động
        string? underReason = SePayRules.FindReconciliationReason(underpaid, BankAccount, order);
        string? overReason = SePayRules.FindReconciliationReason(overpaid, BankAccount, order);

        // Kiểm tra
        Assert.Equal("AMOUNT_MISMATCH", underReason);
        Assert.Equal("AMOUNT_MISMATCH", overReason);
    }

    [Fact]
    public void Pay04_AmountMismatchComesBeforeAlreadyPaid()
    {
        // Chuẩn bị: đơn đã trả và giao dịch lại sai số tiền: lý do sai số tiền đứng trước
        PaymentTransaction transaction = NewTransaction();
        transaction.TransferAmount = 1;
        PaymentOrder order = NewOrder(PaymentOrderStatus.Paid);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Equal("AMOUNT_MISMATCH", reason);
    }

    [Fact]
    public void Ac97_OrderAlreadyPaid_IsAlreadyPaid()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        PaymentOrder order = NewOrder(PaymentOrderStatus.Paid);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Equal("ALREADY_PAID", reason);
    }

    [Fact]
    public void Pay07_CancelledOrder_IsOrderCancelled()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        PaymentOrder order = NewOrder(PaymentOrderStatus.Cancelled);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Equal("ORDER_CANCELLED", reason);
    }

    [Fact]
    public void Pay06_NoTransactionTime_IsTimeUnknown()
    {
        // Chuẩn bị
        PaymentTransaction transaction = NewTransaction();
        transaction.TransactionAt = null;
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Equal("TIME_UNKNOWN", reason);
    }

    [Fact]
    public void Ac30_Pay06_TransactionAtOrAfterExpiry_IsLate_AndJustBeforeIsValid()
    {
        // Chuẩn bị
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending);
        PaymentTransaction justBefore = NewTransaction();
        justBefore.TransactionAt = order.ExpiresAt.AddSeconds(-1);
        PaymentTransaction atExpiry = NewTransaction();
        atExpiry.TransactionAt = order.ExpiresAt;
        PaymentTransaction after = NewTransaction();
        after.TransactionAt = order.ExpiresAt.AddMinutes(5);

        // Hành động
        string? beforeReason = SePayRules.FindReconciliationReason(justBefore, BankAccount, order);
        string? atReason = SePayRules.FindReconciliationReason(atExpiry, BankAccount, order);
        string? afterReason = SePayRules.FindReconciliationReason(after, BankAccount, order);

        // Kiểm tra: đúng hạn là muộn (PAY-06 dùng >=)
        Assert.Null(beforeReason);
        Assert.Equal("LATE_AFTER_EXPIRY", atReason);
        Assert.Equal("LATE_AFTER_EXPIRY", afterReason);
    }

    [Fact]
    public void Pay06_ExpiredOrderWithTransactionBeforeExpiry_IsStillValid()
    {
        // Chuẩn bị: đơn bị đánh dấu Expired (D7) nhưng tiền vào trước hạn
        PaymentTransaction transaction = NewTransaction();
        transaction.TransactionAt = Now.AddMinutes(9);
        PaymentOrder order = NewOrder(PaymentOrderStatus.Expired);

        // Hành động
        string? reason = SePayRules.FindReconciliationReason(transaction, BankAccount, order);

        // Kiểm tra
        Assert.Null(reason);
    }

    [Fact]
    public void Pay07_OnlyNotIncomingSkipsIncident()
    {
        // Chuẩn bị: mọi lý do trong hằng số
        List<string> reasons = new List<string>
        {
            PaymentReconciliationReasons.WrongAccount,
            PaymentReconciliationReasons.CodeMissing,
            PaymentReconciliationReasons.OrderNotFound,
            PaymentReconciliationReasons.AmountMismatch,
            PaymentReconciliationReasons.AlreadyPaid,
            PaymentReconciliationReasons.OrderCancelled,
            PaymentReconciliationReasons.TimeUnknown,
            PaymentReconciliationReasons.LateAfterExpiry,
            "VAULT_CLOSED",
            "ENTITLEMENT_CHANGED",
        };

        // Hành động + Kiểm tra
        Assert.False(SePayRules.ShouldRaiseIncident(PaymentReconciliationReasons.NotIncoming));
        foreach (string reason in reasons)
        {
            Assert.True(SePayRules.ShouldRaiseIncident(reason));
        }
    }

    [Fact]
    public void Pay07_LinkToOrderOnlyAfterTheOrderWasFoundAndCheckedFromAmountOn()
    {
        // Chuẩn bị + Hành động + Kiểm tra: các lý do trước bước so số tiền không gắn đơn
        Assert.False(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.NotIncoming));
        Assert.False(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.WrongAccount));
        Assert.False(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.CodeMissing));
        Assert.False(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.OrderNotFound));
        Assert.True(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.AmountMismatch));
        Assert.True(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.AlreadyPaid));
        Assert.True(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.OrderCancelled));
        Assert.True(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.TimeUnknown));
        Assert.True(SePayRules.ShouldLinkToOrder(PaymentReconciliationReasons.LateAfterExpiry));
        Assert.True(SePayRules.ShouldLinkToOrder("VAULT_CLOSED"));
        Assert.True(SePayRules.ShouldLinkToOrder("ENTITLEMENT_CHANGED"));
    }
}
