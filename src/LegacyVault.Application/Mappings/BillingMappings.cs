using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
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
        else
        {
            response.ServiceStartsAt = null;
        }
        response.ServiceExpiresAt = quote.BaseServiceExpiresAt;
        response.PolicyVersion = BillingPrices.PolicyVersion;
        response.Notice = NoAutoHandoverNotice;
        response.ServerTime = serverTime;
        return response;
    }

    /// <summary>Bảng giá hiện hành từ BillingPrices, không đọc DB.</summary>
    public static BillingPlansResponse ToPlansResponse()
    {
        BillingPlansResponse response = new BillingPlansResponse();
        response.PolicyVersion = BillingPrices.PolicyVersion;
        response.Currency = BillingPrices.Currency;
        response.Notice = NoAutoHandoverNotice;
        response.ExtraSlot.PricePerYear = BillingPrices.SlotPricePerYear;
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
                price.Amount = BillingPrices.GetTierPrice(tier, years);
                price.ServiceDays = years * BillingPrices.DaysPerYear;
                tierResponse.Prices.Add(price);
            }
            response.Tiers.Add(tierResponse);
        }
        return response;
    }
}
