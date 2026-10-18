using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SRS §8: backend tự xác định loại đơn và tự tính giá; thanh toán không tự kích hoạt bàn giao.
/// Bước báo giá không ghi DB. Không log hay lưu số tiền do client gửi vì request không có trường tiền.
/// </remarks>
public sealed class BillingService : IBillingService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IEkycVerificationRepository ekycRepository;
    private readonly TimeProvider timeProvider;

    public BillingService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IEkycVerificationRepository ekycRepository,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.ekycRepository = ekycRepository;
        this.timeProvider = timeProvider;
    }

    public Task<BillingPlansResponse> GetPlansAsync(CancellationToken ct)
    {
        BillingPlansResponse response = BillingMappings.ToPlansResponse();
        return Task.FromResult(response);
    }

    public async Task<BillingQuoteResponse> QuoteAsync(BillingSelectionRequest request, CancellationToken ct)
    {
        // Người đăng nhập → kho → điều kiện (§8.3 bước 1–5).
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();
        BillingEligibility eligibility = await CheckEligibilityAsync(userId, now, ct);

        // Dữ liệu: tính giá bằng đúng hàm mà bước tạo đơn sẽ dùng (D2).
        BillingQuote quote;
        if (eligibility.Kind == PaymentOrderKind.Purchase)
        {
            quote = BillingRules.QuotePurchase(request.Tier, request.Years, request.ExtraSlots);
        }
        else
        {
            quote = BillingRules.QuoteAddon(eligibility.Vault.Entitlement, request.ExtraSlots, now);
        }
        return quote.ToResponse(now);
    }

    /// <summary>
    /// Các kiểm tra chung của báo giá và tạo đơn, theo đúng thứ tự: kho của người dùng (404) → kho đóng →
    /// kế hoạch bị khóa → loại đơn → eKYC Owner (chỉ mua mới).
    /// </summary>
    private async Task<BillingEligibility> CheckEligibilityAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        Vault? found = await vaultRepository.FindByOwnerIdAsync(userId, ct);
        // Chưa có kho → 404 VAULT_NOT_FOUND (SEC-04).
        Vault vault = VaultRules.EnsureOwnedBy(found, userId);

        BillingRules.EnsureVaultAndPlanAllowPayment(vault);

        PaymentOrderKind kind = BillingRules.DetermineKind(vault.Entitlement, now);

        if (kind == PaymentOrderKind.Purchase)
        {
            // §8.3: mua mới cần eKYC Owner đạt trong 90 ngày. Chỉ xét lượt mục đích OwnerEnrollment (EKYC-01).
            EkycVerification? latest = await ekycRepository.FindLatestByUserAndPurposeAsync(
                userId, EkycPurpose.OwnerEnrollment, ct);
            if (!BillingRules.IsOwnerEkycFresh(latest, now))
            {
                throw BillingRules.NotAllowed(PaymentNotAllowedReason.OwnerEkycRequired,
                    "Cần xác minh danh tính (eKYC) của Owner trong vòng 90 ngày trước khi mua dịch vụ.");
            }
        }

        BillingEligibility eligibility = new BillingEligibility();
        eligibility.Vault = vault;
        eligibility.Kind = kind;
        return eligibility;
    }

    /// <summary>Kết quả kiểm tra điều kiện, để bước tạo đơn (Bước 3) dùng lại.</summary>
    private sealed class BillingEligibility
    {
        public Vault Vault { get; set; } = null!;

        public PaymentOrderKind Kind { get; set; }
    }
}
