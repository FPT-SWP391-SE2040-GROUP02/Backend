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
/// Không có job hết hạn đơn ở phiên bản này (D7): khi đọc, tạo hoặc hủy mà thấy đơn Pending đã quá hạn thì đổi Expired.
/// SEC-07: không ghi mã thanh toán, số tài khoản hay số tiền vào nhật ký hoạt động.
/// </remarks>
public sealed class BillingService : IBillingService
{
    /// <summary>Số lần thử tạo đơn khi DB từ chối vì unique (trùng PaymentCode rất hiếm, hoặc request khác tạo đơn trước).</summary>
    private const int MaxCreateAttempts = 3;

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IEkycVerificationRepository ekycRepository;
    private readonly IPaymentOrderRepository paymentOrderRepository;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly IBillingBankSettings bankSettings;
    private readonly TimeProvider timeProvider;

    public BillingService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IEkycVerificationRepository ekycRepository,
        IPaymentOrderRepository paymentOrderRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IBillingBankSettings bankSettings,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.ekycRepository = ekycRepository;
        this.paymentOrderRepository = paymentOrderRepository;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.bankSettings = bankSettings;
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

        // Dữ liệu: tính giá bằng đúng hàm mà bước tạo đơn dùng (D2).
        BillingQuote quote = BuildQuote(request, eligibility, now);
        return quote.ToResponse(now);
    }

    public async Task<CreatePaymentOrderResult> CreateOrderAsync(BillingSelectionRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Quyền + trạng thái: cùng các kiểm tra và cùng cách tính giá như báo giá.
        BillingEligibility eligibility = await CheckEligibilityAsync(userId, now, ct);
        BillingQuote quote = BuildQuote(request, eligibility, now);

        // Chưa cấu hình tài khoản nhận tiền thì không phát đơn: Owner sẽ không có nơi để trả.
        EnsureBankConfigured();

        // Sau DiscardPendingChanges các entity đã nạp bị bỏ, nên chỉ giữ lại các giá trị cần dùng.
        Guid vaultId = eligibility.Vault.Id;

        for (int attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            // PAY-04: mỗi kho tối đa một đơn Pending.
            PaymentOrder? pending = await paymentOrderRepository.FindPendingByVaultIdAsync(vaultId, ct);
            if (pending != null)
            {
                if (BillingRules.IsExpired(pending, now))
                {
                    // D7: đơn cũ quá hạn → Expired, lưu cùng lần lưu với đơn mới bên dưới.
                    pending.Status = PaymentOrderStatus.Expired;
                    pending.UpdatedAt = now;
                }
                else if (BillingRules.IsSameSelection(pending, quote))
                {
                    // §8.3: cùng lựa chọn thì trả lại đơn cũ, không tạo thêm.
                    return NewResult(pending, false, now);
                }
                else
                {
                    throw PendingConflict(pending.Id);
                }
            }

            PaymentOrder order = BuildOrder(vaultId, userId, quote, now);
            auditLogRepository.Add(AuditRules.OwnerAction(
                vaultId, userId, AuditAction.PaymentOrderCreated, AuditTargetType.PaymentOrder, order.Id, null, now));

            // Một lần lưu cho: đổi đơn cũ sang Expired (nếu có), thêm đơn mới, ghi nhật ký (STATE-02).
            bool added = await paymentOrderRepository.TryAddAsync(order, ct);
            if (added)
            {
                return NewResult(order, true, now);
            }

            // Thua do request khác tạo đơn trước, hoặc trùng PaymentCode: bỏ thay đổi chưa lưu rồi đọc lại.
            paymentOrderRepository.DiscardPendingChanges();
        }

        throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
            "Không thể tạo đơn thanh toán lúc này. Vui lòng thử lại.");
    }

    public async Task<PaymentOrderResponse?> GetCurrentOrderAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();
        Vault vault = await LoadVaultAsync(userId, ct);
        Guid vaultId = vault.Id;

        PaymentOrder? pending = await paymentOrderRepository.FindPendingByVaultIdAsync(vaultId, ct);
        if (pending == null)
        {
            return null;
        }
        PaymentOrder current = pending;
        if (BillingRules.IsExpired(current, now))
        {
            current = await MarkExpiredAsync(current, vaultId, now, ct);
        }
        if (current.Status != PaymentOrderStatus.Pending)
        {
            return null;
        }
        return current.ToResponse(bankSettings, now);
    }

    public async Task<PaymentOrderResponse> GetOrderAsync(Guid orderId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();
        Vault vault = await LoadVaultAsync(userId, ct);
        Guid vaultId = vault.Id;

        // Đơn của người khác cũng 404 để không lộ sự tồn tại (SEC-04).
        PaymentOrder? found = await paymentOrderRepository.FindByIdForVaultAsync(vaultId, orderId, ct);
        if (found == null)
        {
            throw BillingRules.OrderNotFound();
        }
        PaymentOrder order = found;
        if (BillingRules.IsExpired(order, now))
        {
            order = await MarkExpiredAsync(order, vaultId, now, ct);
        }
        return order.ToResponse(bankSettings, now);
    }

    public async Task<PaymentOrderResponse> CancelOrderAsync(Guid orderId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();
        Vault vault = await LoadVaultAsync(userId, ct);
        Guid vaultId = vault.Id;

        PaymentOrder? found = await paymentOrderRepository.FindByIdForVaultAsync(vaultId, orderId, ct);
        if (found == null)
        {
            throw BillingRules.OrderNotFound();
        }
        PaymentOrder order = found;

        // Đơn Pending quá hạn: ghi nhận Expired trước, rồi báo không hủy được.
        if (BillingRules.IsExpired(order, now))
        {
            order = await MarkExpiredAsync(order, vaultId, now, ct);
        }

        // Hủy lặp không lỗi: trả lại đơn đã hủy.
        if (order.Status == PaymentOrderStatus.Cancelled)
        {
            return order.ToResponse(bankSettings, now);
        }
        if (order.Status == PaymentOrderStatus.Paid)
        {
            throw BillingRules.NotPayable(PaymentOrderNotPayableReason.Paid, "Đơn đã được thanh toán nên không thể hủy.");
        }
        if (order.Status == PaymentOrderStatus.Expired)
        {
            throw BillingRules.NotPayable(PaymentOrderNotPayableReason.Expired, "Đơn đã hết hạn thanh toán.");
        }

        // Pending còn hạn → hủy. RowVersion chặn hủy song song với thanh toán (EDIT_CONFLICT).
        order.Status = PaymentOrderStatus.Cancelled;
        order.CancelledAt = now;
        order.UpdatedAt = now;
        auditLogRepository.Add(AuditRules.OwnerAction(
            vaultId, userId, AuditAction.PaymentOrderCancelled, AuditTargetType.PaymentOrder, order.Id, null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToResponse(bankSettings, now);
    }

    /// <summary>
    /// Các kiểm tra chung của báo giá và tạo đơn, theo đúng thứ tự: kho của người dùng (404) → kho đóng →
    /// kế hoạch bị khóa → loại đơn → eKYC Owner (chỉ mua mới).
    /// </summary>
    private async Task<BillingEligibility> CheckEligibilityAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        Vault vault = await LoadVaultAsync(userId, ct);

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

    /// <summary>Kho của người đang đăng nhập; chưa có kho → 404 VAULT_NOT_FOUND (SEC-04).</summary>
    private async Task<Vault> LoadVaultAsync(Guid userId, CancellationToken ct)
    {
        Vault? found = await vaultRepository.FindByOwnerIdAsync(userId, ct);
        return VaultRules.EnsureOwnedBy(found, userId);
    }

    /// <summary>Tính giá theo loại đơn đã xác định; báo giá và tạo đơn dùng chung (D2).</summary>
    private static BillingQuote BuildQuote(BillingSelectionRequest request, BillingEligibility eligibility, DateTimeOffset now)
    {
        if (eligibility.Kind == PaymentOrderKind.Purchase)
        {
            return BillingRules.QuotePurchase(request.Tier, request.Years, request.ExtraSlots);
        }
        return BillingRules.QuoteAddon(eligibility.Vault.Entitlement, request.ExtraSlots, now);
    }

    /// <summary>Dựng đơn Pending từ báo giá: giá, dung lượng, số suất, phiên bản chính sách đều được chốt tại đây (PAY-11).</summary>
    private static PaymentOrder BuildOrder(Guid vaultId, Guid ownerId, BillingQuote quote, DateTimeOffset now)
    {
        PaymentOrder order = new PaymentOrder();
        order.VaultId = vaultId;
        order.OwnerId = ownerId;
        order.Kind = quote.Kind;
        order.Tier = quote.Tier;
        order.Years = quote.Years;
        order.ExtraSlots = quote.ExtraSlots;
        order.TierAmount = quote.TierAmount;
        order.SlotAmount = quote.SlotAmount;
        order.TotalAmount = quote.TotalAmount;
        order.StorageQuotaBytes = quote.StorageQuotaBytes;
        order.IncludedBeneficiarySlots = quote.IncludedBeneficiarySlots;
        order.BaseServiceExpiresAt = quote.BaseServiceExpiresAt;
        order.PolicyVersion = BillingPrices.PolicyVersion;
        order.PaymentCode = BillingRules.NewPaymentCode();
        order.Status = PaymentOrderStatus.Pending;
        // ADDON-03: đơn mua thêm hết hạn ở mốc sớm hơn giữa 15 phút và hết kỳ hiện tại.
        order.ExpiresAt = BillingRules.ComputeOrderExpiresAt(quote.Kind, now, quote.BaseServiceExpiresAt);
        order.CreatedAt = now;
        order.UpdatedAt = now;
        return order;
    }

    /// <summary>
    /// D7: đổi đơn Pending quá hạn sang Expired và lưu. Nếu request khác vừa sửa cùng đơn (trả tiền, hủy, hoặc cũng
    /// đánh dấu Expired) thì RowVersion lệch: bỏ thay đổi của mình và trả trạng thái mới nhất trong DB.
    /// </summary>
    private async Task<PaymentOrder> MarkExpiredAsync(PaymentOrder order, Guid vaultId, DateTimeOffset now, CancellationToken ct)
    {
        Guid orderId = order.Id;
        order.Status = PaymentOrderStatus.Expired;
        order.UpdatedAt = now;
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return order;
        }
        catch (AppException exception) when (exception.Code == ErrorCode.EditConflict)
        {
            paymentOrderRepository.DiscardPendingChanges();
            PaymentOrder? fresh = await paymentOrderRepository.FindByIdForVaultAsync(vaultId, orderId, ct);
            if (fresh == null)
            {
                throw BillingRules.OrderNotFound();
            }
            return fresh;
        }
    }

    private CreatePaymentOrderResult NewResult(PaymentOrder order, bool created, DateTimeOffset now)
    {
        CreatePaymentOrderResult result = new CreatePaymentOrderResult();
        result.Created = created;
        result.Order = order.ToResponse(bankSettings, now);
        return result;
    }

    private static AppException PendingConflict(Guid pendingOrderId)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["orderId"] = pendingOrderId;
        return new AppException(AppErrorType.Conflict, ErrorCode.PaymentOrderPending,
            "Kho đang có một đơn chờ thanh toán với lựa chọn khác. Hủy đơn cũ trước khi tạo đơn mới.", extensions);
    }

    private void EnsureBankConfigured()
    {
        if (string.IsNullOrWhiteSpace(bankSettings.BankCode) || string.IsNullOrWhiteSpace(bankSettings.AccountNumber))
        {
            // Lỗi cấu hình máy chủ, không phải lỗi người dùng: để GlobalExceptionHandler trả 500 chung, không lộ chi tiết.
            throw new InvalidOperationException("Chưa cấu hình tài khoản nhận tiền (Billing:Bank).");
        }
    }

    /// <summary>Kết quả kiểm tra điều kiện, để báo giá và tạo đơn dùng chung.</summary>
    private sealed class BillingEligibility
    {
        public Vault Vault { get; set; } = null!;

        public PaymentOrderKind Kind { get; set; }
    }
}
