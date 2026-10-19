using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class PaymentTransactionMappings
{
    /// <summary>
    /// Chỉ các trường Admin cần để đối chiếu (ADMIN-09). Cố ý không chép Content, AccountNumber và RowVersion.
    /// </summary>
    public static AdminPaymentTransactionResponse ToAdminResponse(this PaymentTransaction transaction)
    {
        AdminPaymentTransactionResponse response = new AdminPaymentTransactionResponse();
        response.Id = transaction.Id;
        response.Provider = transaction.Provider;
        response.Environment = transaction.Environment;
        response.ProviderTransactionId = transaction.ProviderTransactionId;
        response.Status = transaction.Status.ToApiString();
        response.ReconciliationReason = transaction.ReconciliationReason;
        response.TransferType = transaction.TransferType.ToUpperInvariant();
        response.TransferAmount = transaction.TransferAmount;
        response.Currency = BillingPrices.Currency;
        response.PaymentOrderId = transaction.PaymentOrderId;
        response.PaymentCode = transaction.MatchedPaymentCode;
        response.Gateway = transaction.Gateway;
        response.ReferenceCode = transaction.ReferenceCode;
        response.TransactionAt = transaction.TransactionAt;
        response.ReceivedAt = transaction.ReceivedAt;
        response.ProcessedAt = transaction.ProcessedAt;
        return response;
    }
}
