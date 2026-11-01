using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SRS §8.3 bước 6–9, §8.5, PAY-02..07: lưu giao dịch trước (lần lưu 1), rồi đối chiếu với đơn và cấp quyền
/// trong một transaction Serializable (lần lưu 2). Lỗi bất ngờ ở lần 2 làm giao dịch còn Received để SePay gửi lại
/// thì xử lý tiếp. SEC-07: không ghi log key, số tài khoản hay nội dung chuyển khoản; sự cố chỉ chứa mã lý do.
/// </remarks>
public sealed class SePayWebhookService : ISePayWebhookService
{
    public const string ProviderName = "SePay";

    /// <summary>Loại đối tượng của sự cố thanh toán; TargetId là Id giao dịch.</summary>
    public const string IncidentTargetType = "PaymentTransaction";

    private const string TransferTypeIn = "in";
    private const string TransferTypeOut = "out";

    // Độ dài cột tương ứng trong PaymentTransactions; chuỗi dài hơn bị cắt để lưu không lỗi.
    private const int MaxGatewayLength = 64;
    private const int MaxAccountNumberLength = 32;
    private const int MaxCodeLength = 32;
    private const int MaxContentLength = 512;
    private const int MaxReferenceCodeLength = 64;

    private readonly IBillingSePaySettings sePaySettings;
    private readonly IBillingBankSettings bankSettings;
    private readonly IPaymentTransactionRepository transactionRepository;
    private readonly IPaymentOrderRepository orderRepository;
    private readonly PaymentGranter paymentGranter;
    private readonly IIncidentService incidentService;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public SePayWebhookService(
        IBillingSePaySettings sePaySettings,
        IBillingBankSettings bankSettings,
        IPaymentTransactionRepository transactionRepository,
        IPaymentOrderRepository orderRepository,
        PaymentGranter paymentGranter,
        IIncidentService incidentService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.sePaySettings = sePaySettings;
        this.bankSettings = bankSettings;
        this.transactionRepository = transactionRepository;
        this.orderRepository = orderRepository;
        this.paymentGranter = paymentGranter;
        this.incidentService = incidentService;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task HandleAsync(string? authorizationHeader, SePayWebhookRequest request, CancellationToken ct)
    {
        // Bước 1 (8.4): xác thực nguồn gửi. Sai thì không lưu và không tiết lộ lý do cụ thể.
        if (!SePayRules.IsApiKeyValid(sePaySettings.WebhookApiKey, authorizationHeader))
        {
            throw new AppException(AppErrorType.Unauthorized, ErrorCode.WebhookUnauthorized,
                "Webhook không được xác thực.");
        }

        // Bước 2: dữ liệu tối thiểu.
        ValidateRequest(request);
        long providerTransactionId = request.Id!.Value;

        // Bước 3: webhook gửi lặp (SePay gửi lại khi không nhận được success) không tạo thêm dòng (AC-29).
        PaymentTransaction? stored = await transactionRepository.FindByProviderTransactionIdAsync(
            ProviderName, providerTransactionId, ct);

        // Bước 4: chưa có thì lưu giao dịch Received trước khi làm bất cứ điều gì khác (PAY-05).
        if (stored == null)
        {
            PaymentTransaction fresh = BuildTransaction(request, providerTransactionId);
            bool added = await transactionRepository.TryAddAsync(fresh, ct);
            if (added)
            {
                stored = fresh;
            }
            else
            {
                // Request song song cùng giao dịch đã lưu trước: đọc lại dòng đó.
                stored = await transactionRepository.FindByProviderTransactionIdAsync(ProviderName, providerTransactionId, ct);
            }
        }
        if (stored == null)
        {
            throw new InvalidOperationException("Không đọc lại được giao dịch vừa lưu.");
        }

        // Đã đối chiếu xong (Matched, Resolved): trả success, không xử lý lại (AC-29).
        // Nếu là ReconciliationRequired nhưng chưa từng gắn vào đơn nào (PaymentOrderId == null),
        // cho phép thử đối chiếu lại khi SePay gửi lại webhook hoặc bóc tách mã được cải thiện.
        bool canReconcile = stored.Status == PaymentTransactionStatus.Received
            || (stored.Status == PaymentTransactionStatus.ReconciliationRequired && stored.PaymentOrderId == null);

        if (!canReconcile)
        {
            return;
        }

        // Cập nhật lại tài khoản nhận (VA hoặc tài khoản chính) từ lần gửi này, giống như với mã thanh toán bên dưới
        stored.AccountNumber = SePayRules.ResolveReceivingAccount(request.AccountNumber, request.SubAccount);

        // Cập nhật lại MatchedPaymentCode nếu trước đó chưa tìm thấy hoặc bị cắt ngắn
        string? resolvedCode = SePayRules.FindPaymentCode(request.Code, request.Content);
        if (!string.IsNullOrWhiteSpace(resolvedCode) && resolvedCode != stored.MatchedPaymentCode)
        {
            stored.MatchedPaymentCode = resolvedCode;
        }

        // Bước 5: đối chiếu + cấp quyền. Serializable vì cùng đọc và ghi quyền dịch vụ của kho như dev-pay;
        // lỗi giữa chừng rollback, giao dịch vẫn Received và SePay sẽ gửi lại.
        PaymentTransaction toReconcile = stored;
        await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            await ReconcileAsync(toReconcile, ct);
            return true;
        }, ct);
    }

    /// <summary>
    /// Đối chiếu một giao dịch Received với đơn theo SePayRules.FindReconciliationReason, rồi cấp quyền (PaymentGranter)
    /// hoặc đánh dấu cần đối chiếu và mở sự cố. Lưu đúng một lần ở cuối. Không bắt lỗi bất ngờ: để lỗi đi ra thành 500/409.
    /// </summary>
    private async Task ReconcileAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        PaymentOrder? order = null;
        if (!string.IsNullOrWhiteSpace(transaction.MatchedPaymentCode))
        {
            order = await orderRepository.FindByPaymentCodeAsync(transaction.MatchedPaymentCode, ct);
        }

        string? reason = SePayRules.FindReconciliationReason(transaction, bankSettings.AccountNumber, order);

        if (reason == null)
        {
            if (order == null || transaction.TransactionAt == null)
            {
                // Không xảy ra vì FindReconciliationReason đã chặn; giữ nhánh này để không cấp quyền khi thiếu dữ liệu.
                reason = PaymentReconciliationReasons.OrderNotFound;
            }
            else
            {
                reason = await TryGrantAsync(order, transaction.TransactionAt.Value, ct);
            }
        }

        if (reason == null)
        {
            // Khớp: PaymentGranter đã chuyển đơn sang Paid và ghi nhật ký PaymentPaid (hệ thống).
            transaction.Status = PaymentTransactionStatus.Matched;
            transaction.PaymentOrderId = order!.Id;
            transaction.ProcessedAt = now;
        }
        else
        {
            await MarkReconciliationRequiredAsync(transaction, order, reason, now, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cấp quyền cho đơn. Trả null khi đã cấp; trả mã lý do khi PaymentGranter từ chối (kho đóng, quyền dịch vụ đã đổi, ...).
    /// PaymentGranter kiểm tra hết rồi mới ghi nên khi nó từ chối thì chưa có thay đổi dở dang nào.
    /// </summary>
    private async Task<string?> TryGrantAsync(PaymentOrder order, DateTimeOffset paidAt, CancellationToken ct)
    {
        try
        {
            bool granted = await paymentGranter.GrantAsync(order, paidAt, ct);
            if (!granted)
            {
                return PaymentReconciliationReasons.AlreadyPaid;
            }
            return null;
        }
        catch (AppException exception) when (exception.Code == ErrorCode.PaymentOrderNotPayable)
        {
            object? reasonValue = null;
            exception.Extensions.TryGetValue("reason", out reasonValue);
            string? reasonText = reasonValue as string;
            if (string.IsNullOrWhiteSpace(reasonText))
            {
                return "NOT_PAYABLE";
            }
            return reasonText;
        }
    }

    private async Task MarkReconciliationRequiredAsync(
        PaymentTransaction transaction, PaymentOrder? order, string reason, DateTimeOffset now, CancellationToken ct)
    {
        transaction.Status = PaymentTransactionStatus.ReconciliationRequired;
        transaction.ReconciliationReason = reason;
        transaction.ProcessedAt = now;

        Guid? vaultId = null;
        if (order != null)
        {
            vaultId = order.VaultId;
            if (SePayRules.ShouldLinkToOrder(reason))
            {
                transaction.PaymentOrderId = order.Id;
            }
        }

        if (SePayRules.ShouldRaiseIncident(reason))
        {
            // E7: chỉ lưu lý do, không có số tiền, mã thanh toán hay số tài khoản (SEC-07). RaiseAsync không tự lưu.
            string summary = "Giao dịch thanh toán cần đối chiếu, lý do: " + reason;
            await incidentService.RaiseAsync(
                IncidentType.Payment, vaultId, IncidentTargetType, transaction.Id, summary, null, ct);
        }
    }

    private static void ValidateRequest(SePayWebhookRequest request)
    {
        if (request.Id == null || request.Id.Value <= 0)
        {
            throw ValidationErrors.For("id", "ID giao dịch phải lớn hơn 0.");
        }
        if (request.TransferAmount == null || request.TransferAmount.Value < 0)
        {
            throw ValidationErrors.For("transferAmount", "Số tiền không hợp lệ.");
        }
        if (string.IsNullOrWhiteSpace(request.AccountNumber)
            || request.AccountNumber.Trim().Length > MaxAccountNumberLength)
        {
            throw ValidationErrors.For("accountNumber", "Số tài khoản không hợp lệ.");
        }
        if (!string.IsNullOrWhiteSpace(request.SubAccount) && request.SubAccount.Trim().Length > MaxAccountNumberLength)
        {
            throw ValidationErrors.For("subAccount", "Số tài khoản ảo không hợp lệ.");
        }
        if (string.IsNullOrWhiteSpace(request.TransferType))
        {
            throw ValidationErrors.For("transferType", "Thiếu loại giao dịch.");
        }
        string transferType = request.TransferType.Trim();
        bool isIn = string.Equals(transferType, TransferTypeIn, StringComparison.OrdinalIgnoreCase);
        bool isOut = string.Equals(transferType, TransferTypeOut, StringComparison.OrdinalIgnoreCase);
        if (!isIn && !isOut)
        {
            throw ValidationErrors.For("transferType", "Loại giao dịch chỉ nhận in hoặc out.");
        }
    }

    private PaymentTransaction BuildTransaction(SePayWebhookRequest request, long providerTransactionId)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        string transferType = TransferTypeOut;
        if (string.Equals(request.TransferType!.Trim(), TransferTypeIn, StringComparison.OrdinalIgnoreCase))
        {
            transferType = TransferTypeIn;
        }

        PaymentTransaction transaction = new PaymentTransaction();
        transaction.Provider = ProviderName;
        transaction.Environment = sePaySettings.Environment;
        transaction.ProviderTransactionId = providerTransactionId;
        transaction.Gateway = SePayRules.Truncate(request.Gateway, MaxGatewayLength);
        // PAY-03: lưu số tài khoản mà tiền thực sự đã vào (VA nếu có), không lưu số tài khoản chính khi đã qua VA.
        transaction.AccountNumber = SePayRules.ResolveReceivingAccount(request.AccountNumber, request.SubAccount);
        transaction.Code = SePayRules.Truncate(request.Code, MaxCodeLength);
        transaction.MatchedPaymentCode = SePayRules.FindPaymentCode(request.Code, request.Content);
        transaction.Content = SePayRules.Truncate(request.Content, MaxContentLength);
        transaction.TransferType = transferType;
        transaction.TransferAmount = request.TransferAmount!.Value;
        transaction.ReferenceCode = SePayRules.Truncate(request.ReferenceCode, MaxReferenceCodeLength);
        transaction.TransactionAt = SePayRules.ParseVietnamTime(request.TransactionDate);
        transaction.ReceivedAt = now;
        transaction.Status = PaymentTransactionStatus.Received;
        return transaction;
    }
}
