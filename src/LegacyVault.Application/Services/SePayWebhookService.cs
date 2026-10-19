using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SRS §8.3 bước 6, §8.5, PAY-05: lưu giao dịch trước, đối chiếu với đơn sau (đối chiếu làm ở bước kế tiếp của H2:
/// hiện giao dịch dừng ở Received). SEC-07: không ghi log key, số tài khoản hay nội dung chuyển khoản.
/// </remarks>
public sealed class SePayWebhookService : ISePayWebhookService
{
    public const string ProviderName = "SePay";

    private const string TransferTypeIn = "in";
    private const string TransferTypeOut = "out";

    // Độ dài cột tương ứng trong PaymentTransactions; chuỗi dài hơn bị cắt để lưu không lỗi.
    private const int MaxGatewayLength = 64;
    private const int MaxAccountNumberLength = 32;
    private const int MaxCodeLength = 32;
    private const int MaxContentLength = 512;
    private const int MaxReferenceCodeLength = 64;

    private readonly IBillingSePaySettings sePaySettings;
    private readonly IPaymentTransactionRepository transactionRepository;
    private readonly TimeProvider timeProvider;

    public SePayWebhookService(
        IBillingSePaySettings sePaySettings,
        IPaymentTransactionRepository transactionRepository,
        TimeProvider timeProvider)
    {
        this.sePaySettings = sePaySettings;
        this.transactionRepository = transactionRepository;
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
        PaymentTransaction? existing = await transactionRepository.FindByProviderTransactionIdAsync(
            ProviderName, providerTransactionId, ct);
        if (existing != null)
        {
            return;
        }

        // Bước 4: lưu giao dịch Received trước khi làm bất cứ điều gì khác (PAY-05).
        PaymentTransaction transaction = BuildTransaction(request, providerTransactionId);
        await transactionRepository.TryAddAsync(transaction, ct);

        // TryAddAsync trả false nghĩa là request song song cùng giao dịch đã lưu trước; dòng đã có nên vẫn thành công.
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
        transaction.AccountNumber = request.AccountNumber!.Trim();
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
