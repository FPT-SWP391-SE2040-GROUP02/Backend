using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class BillingMappings
{
    /// <summary>Dòng báo cho Owner trước khi trả tiền (§8.3 bước 3).</summary>
    public const string NoAutoHandoverNotice = "Thanh toán không tự kích hoạt bàn giao.";

    public static BillingQuoteResponse ToResponse(this BillingQuote quote, DateTimeOffset serverTime)
    {
        BillingQuoteResponse response = new BillingQuoteResponse();
        response.Kind = quote.Kind.ToApiString();
        response.Tier = quote.Tier.ToApiString();
        response.Years = quote.Years;
        response.ExtraSlots = quote.ExtraSlots;
        response.TierAmount = quote.TierAmount;
        response.SlotAmount = quote.SlotAmount;
        response.TotalAmount = quote.TotalAmount;
        response.Currency = BillingPrices.Currency;
        response.StorageQuotaBytes = quote.StorageQuotaBytes;
        response.IncludedBeneficiarySlots = quote.IncludedBeneficiarySlots;
        response.BeneficiarySlots = quote.TotalBeneficiarySlots;
        response.ServiceDays = quote.ServiceDays;
        if (quote.Kind == PaymentOrderKind.Purchase)
        {
            response.ServiceStartsAt = "ON_PAYMENT";
        }
        else if (quote.Kind == PaymentOrderKind.Renewal)
        {
            // Còn hạn: nối tiếp hạn cũ (serviceExpiresAt là hạn cũ); đã hết hạn: tính từ lúc thanh toán.
            if (quote.BaseServiceExpiresAt != null)
            {
                response.ServiceStartsAt = "AFTER_CURRENT_TERM";
            }
            else
            {
                response.ServiceStartsAt = "ON_PAYMENT";
            }
        }
        else
        {
            response.ServiceStartsAt = null;
        }
        response.ServiceExpiresAt = quote.BaseServiceExpiresAt;
        response.PolicyVersion = quote.PolicyVersion;
        response.Notice = NoAutoHandoverNotice;
        response.ServerTime = serverTime;
        return response;
    }

    /// <summary>
    /// Đơn → dòng lịch sử. Đơn Pending đã tới hạn hiện là EXPIRED để Owner thấy đúng trạng thái dù chưa có job hết hạn
    /// (D7); không ghi DB.
    /// </summary>
    public static PaymentOrderSummaryResponse ToSummaryResponse(this PaymentOrder order, DateTimeOffset now)
    {
        PaymentOrderSummaryResponse response = new PaymentOrderSummaryResponse();
        response.Id = order.Id;
        response.Kind = order.Kind.ToApiString();
        if (BillingRules.IsExpired(order, now))
        {
            response.Status = PaymentOrderStatus.Expired.ToApiString();
        }
        else
        {
            response.Status = order.Status.ToApiString();
        }
        response.Tier = order.Tier.ToApiString();
        response.Years = order.Years;
        response.ExtraSlots = order.ExtraSlots;
        response.TotalAmount = order.TotalAmount;
        response.Currency = BillingPrices.Currency;
        response.PaymentCode = order.PaymentCode;
        response.CreatedAt = order.CreatedAt;
        response.ExpiresAt = order.ExpiresAt;
        response.PaidAt = order.PaidAt;
        response.CancelledAt = order.CancelledAt;
        return response;
    }

    /// <summary>
    /// Đơn → response. QR chỉ có khi đơn còn Pending. Link QR ghép theo mẫu SePay, mọi giá trị đều được escape.
    /// </summary>
    public static PaymentOrderResponse ToResponse(
        this PaymentOrder order, IBillingBankSettings bank, DateTimeOffset serverTime)
    {
        PaymentOrderResponse response = new PaymentOrderResponse();
        response.Id = order.Id;
        response.Kind = order.Kind.ToApiString();
        response.Status = order.Status.ToApiString();
        response.Tier = order.Tier.ToApiString();
        response.Years = order.Years;
        response.ExtraSlots = order.ExtraSlots;
        response.TierAmount = order.TierAmount;
        response.SlotAmount = order.SlotAmount;
        response.TotalAmount = order.TotalAmount;
        response.Currency = BillingPrices.Currency;
        response.StorageQuotaBytes = order.StorageQuotaBytes;
        response.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        response.PaymentCode = order.PaymentCode;
        response.TransferContent = order.PaymentCode;
        response.Bank.BankCode = bank.BankCode;
        response.Bank.AccountNumber = bank.AccountNumber;
        response.Bank.AccountName = bank.AccountName;
        if (order.Status == PaymentOrderStatus.Pending)
        {
            response.QrImageUrl = BuildQrImageUrl(order, bank);
        }
        else
        {
            response.QrImageUrl = null;
        }
        response.ExpiresAt = order.ExpiresAt;
        response.CreatedAt = order.CreatedAt;
        response.PaidAt = order.PaidAt;
        response.CancelledAt = order.CancelledAt;
        response.ServerTime = serverTime;
        response.Notice = NoAutoHandoverNotice;
        return response;
    }

    private static string BuildQrImageUrl(PaymentOrder order, IBillingBankSettings bank)
    {
        return "https://qr.sepay.vn/img?acc=" + Uri.EscapeDataString(bank.AccountNumber)
            + "&bank=" + Uri.EscapeDataString(bank.BankCode)
            + "&amount=" + order.TotalAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "&des=" + Uri.EscapeDataString(order.PaymentCode);
    }

    /// <summary>Bảng giá hiện hành từ cấu hình Billing:Pricing, không đọc DB.</summary>
    public static BillingPlansResponse ToPlansResponse(IBillingPriceSettings prices)
    {
        BillingPlansResponse response = new BillingPlansResponse();
        response.PolicyVersion = prices.PolicyVersion;
        response.Currency = BillingPrices.Currency;
        response.Notice = NoAutoHandoverNotice;
        response.ExtraSlot.PricePerYear = prices.SlotPricePerYear;
        response.ExtraSlot.MaxPerOrder = BillingPrices.MaxExtraSlotsPerOrder;

        foreach (ServiceTier tier in BillingPrices.SellableTiers)
        {
            BillingTierResponse tierResponse = new BillingTierResponse();
            tierResponse.Tier = tier.ToApiString();
            tierResponse.StorageQuotaBytes = BillingPrices.GetStorageQuotaBytes(tier);
            tierResponse.IncludedBeneficiarySlots = BillingPrices.GetIncludedSlots(tier);
            foreach (int years in BillingPrices.SupportedYears)
            {
                BillingPriceResponse price = new BillingPriceResponse();
                price.Years = years;
                price.Amount = prices.GetTierPrice(tier, years);
                price.ServiceDays = years * BillingPrices.DaysPerYear;
                tierResponse.Prices.Add(price);
            }
            response.Tiers.Add(tierResponse);
        }
        return response;
    }
}
