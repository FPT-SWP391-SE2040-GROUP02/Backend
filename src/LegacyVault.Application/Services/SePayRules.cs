using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần cho webhook SePay (SRS §8.3, §8.5, PAY-01, PAY-05): kiểm key, tìm mã thanh toán, đổi giờ VN.
/// Không đọc DB, không log; mọi giá trị đưa vào là dữ liệu nhạy cảm (key, nội dung chuyển khoản).
/// </summary>
public static class SePayRules
{
    /// <summary>Tên scheme trong header Authorization mà SePay gửi: "Apikey &lt;key&gt;".</summary>
    private const string ApiKeyScheme = "Apikey";

    /// <summary>Độ dài tối đa của MatchedPaymentCode (cột PaymentTransactions.MatchedPaymentCode).</summary>
    private const int MaxPaymentCodeLength = 16;

    /// <summary>SePay gửi transactionDate theo giờ Việt Nam (UTC+7), định dạng yyyy-MM-dd HH:mm:ss.</summary>
    private const string TransactionDateFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Header Authorization phải đúng dạng "Apikey &lt;key&gt;" và key khớp cấu hình. Chưa cấu hình key (rỗng) → luôn false,
    /// nên server chưa sẵn sàng không bao giờ nhận giao dịch. So sánh thời gian cố định để không lộ key qua thời gian xử lý.
    /// </summary>
    public static bool IsApiKeyValid(string? configuredKey, string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        string header = authorizationHeader.Trim();
        int separatorIndex = header.IndexOf(' ');
        if (separatorIndex <= 0)
        {
            return false;
        }

        string scheme = header.Substring(0, separatorIndex);
        if (!string.Equals(scheme, ApiKeyScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string presentedKey = header.Substring(separatorIndex + 1).Trim();
        if (presentedKey.Length == 0)
        {
            return false;
        }

        byte[] expectedBytes = Encoding.UTF8.GetBytes(configuredKey);
        byte[] presentedBytes = Encoding.UTF8.GetBytes(presentedKey);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }

    /// <summary>
    /// Số tài khoản mà tiền thực sự đã vào (PAY-03): có subAccount (tài khoản ảo/VA) thì dùng subAccount, vì tài khoản chính
    /// lúc này chỉ là tài khoản đứng sau VA; không có thì dùng accountNumber. Kết quả đã cắt khoảng trắng đầu cuối.
    /// </summary>
    public static string ResolveReceivingAccount(string? accountNumber, string? subAccount)
    {
        if (!string.IsNullOrWhiteSpace(subAccount))
        {
            return subAccount.Trim();
        }
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return string.Empty;
        }
        return accountNumber.Trim();
    }

    /// <summary>
    /// E6: dùng trường code của SePay nếu có đúng độ dài chuẩn (10 ký tự: LV + 8); nếu không thì tìm mẫu
    /// LV + 8 ký tự trong nội dung chuyển khoản (kể cả trường hợp ngân hàng/ví Momo chèn khoảng trắng ngắt quãng).
    /// </summary>
    public static string? FindPaymentCode(string? code, string? content)
    {
        int codeLength = BillingRules.PaymentCodePrefix.Length + BillingRules.PaymentCodeRandomLength;

        // Nếu trường code của SePay đã bóc được đúng 10 ký tự chuẩn: nhận ngay
        if (!string.IsNullOrWhiteSpace(code))
        {
            string trimmedCode = code.Trim().ToUpperInvariant();
            if (trimmedCode.Length == codeLength && trimmedCode.StartsWith(BillingRules.PaymentCodePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmedCode;
            }
        }

        // Tìm trong nội dung chuyển khoản (content)
        if (!string.IsNullOrEmpty(content))
        {
            string? found = MatchCodeFromContent(content, codeLength);
            if (found != null)
            {
                return found;
            }

            // Nếu ngân hàng hoặc ví (Momo) chèn khoảng trắng làm ngắt mã (ví dụ: "LVLVXW6NC 5"):
            // Loại bỏ khoảng trắng và tìm lại
            string compactContent = string.Concat(content.Where(c => !char.IsWhiteSpace(c)));
            found = MatchCodeFromContent(compactContent, codeLength);
            if (found != null)
            {
                return found;
            }
        }

        // Fallback: nếu không tìm thấy trong content, dùng code SePay gửi (nếu có và <= 16 ký tự)
        if (!string.IsNullOrWhiteSpace(code))
        {
            string trimmedCode = code.Trim();
            if (trimmedCode.Length <= MaxPaymentCodeLength)
            {
                return trimmedCode.ToUpperInvariant();
            }
        }

        return null;
    }

    private static string? MatchCodeFromContent(string text, int codeLength)
    {
        string upperContent = text.ToUpperInvariant();
        for (int start = 0; start + codeLength <= upperContent.Length; start++)
        {
            int prefixCompare = string.CompareOrdinal(
                upperContent, start, BillingRules.PaymentCodePrefix, 0, BillingRules.PaymentCodePrefix.Length);
            if (prefixCompare != 0)
            {
                continue;
            }

            bool allValid = true;
            for (int offset = BillingRules.PaymentCodePrefix.Length; offset < codeLength; offset++)
            {
                if (BillingRules.PaymentCodeAlphabet.IndexOf(upperContent[start + offset]) < 0)
                {
                    allValid = false;
                    break;
                }
            }
            if (allValid)
            {
                return upperContent.Substring(start, codeLength);
            }
        }
        return null;
    }

    /// <summary>
    /// Đổi transactionDate (giờ VN, yyyy-MM-dd HH:mm:ss) sang UTC (PAY-06 so với hạn đơn). Sai định dạng → null,
    /// khi đó giao dịch không được cấp tự động (đối chiếu thủ công).
    /// </summary>
    public static DateTimeOffset? ParseVietnamTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        DateTime local;
        bool parsed = DateTime.TryParseExact(
            text.Trim(), TransactionDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
        if (!parsed)
        {
            return null;
        }

        DateTimeOffset vietnamTime = new DateTimeOffset(local, VietnamOffset);
        return vietnamTime.ToUniversalTime();
    }

    /// <summary>
    /// Thứ tự đối chiếu giao dịch với đơn (SRS §8.3 bước 6–8, PAY-03..07), gặp điều nào thì dừng và trả lý do đó.
    /// Trả null khi giao dịch hợp lệ để cấp quyền (PaymentGranter còn kiểm tra thêm kho đóng, quyền dịch vụ đã đổi).
    /// order là đơn tìm theo mã thanh toán (null nếu không có mã hoặc không tìm thấy đơn).
    /// Đơn đã hết hạn (Expired do D7) mà tiền vào trước hạn vẫn hợp lệ (PAY-06).
    /// </summary>
    public static string? FindReconciliationReason(
        PaymentTransaction transaction, string bankAccountNumber, PaymentOrder? order)
    {
        // Chỉ tiền vào mới có thể thanh toán đơn.
        bool isIncoming = string.Equals(transaction.TransferType, "in", StringComparison.OrdinalIgnoreCase);
        if (!isIncoming)
        {
            return PaymentReconciliationReasons.NotIncoming;
        }

        // PAY-03: tiền phải vào đúng tài khoản nhận; chưa cấu hình tài khoản nhận thì không giao dịch nào khớp.
        if (string.IsNullOrWhiteSpace(bankAccountNumber))
        {
            return PaymentReconciliationReasons.WrongAccount;
        }
        bool sameAccount = string.Equals(
            transaction.AccountNumber.Trim(), bankAccountNumber.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!sameAccount)
        {
            return PaymentReconciliationReasons.WrongAccount;
        }

        if (string.IsNullOrWhiteSpace(transaction.MatchedPaymentCode))
        {
            return PaymentReconciliationReasons.CodeMissing;
        }
        if (order == null)
        {
            return PaymentReconciliationReasons.OrderNotFound;
        }

        // AC-28: đúng từng đồng, thiếu hay thừa đều chuyển đối chiếu.
        if (transaction.TransferAmount != order.TotalAmount)
        {
            return PaymentReconciliationReasons.AmountMismatch;
        }
        if (order.Status == PaymentOrderStatus.Paid)
        {
            return PaymentReconciliationReasons.AlreadyPaid;
        }
        if (order.Status == PaymentOrderStatus.Cancelled)
        {
            return PaymentReconciliationReasons.OrderCancelled;
        }

        // PAY-06: so giờ tiền vào với hạn đơn, không dựa vào trạng thái Expired.
        if (transaction.TransactionAt == null)
        {
            return PaymentReconciliationReasons.TimeUnknown;
        }
        if (transaction.TransactionAt.Value >= order.ExpiresAt)
        {
            return PaymentReconciliationReasons.LateAfterExpiry;
        }
        return null;
    }

    /// <summary>
    /// Có gắn giao dịch lệch vào đơn không: chỉ khi đã qua các bước chưa cần đến đơn (tiền vào, đúng tài khoản, có mã, thấy đơn),
    /// để Admin biết giao dịch thuộc đơn nào. Tiền ra, sai tài khoản, thiếu mã, không thấy đơn thì không gắn.
    /// </summary>
    public static bool ShouldLinkToOrder(string reason)
    {
        if (reason == PaymentReconciliationReasons.NotIncoming)
        {
            return false;
        }
        if (reason == PaymentReconciliationReasons.WrongAccount)
        {
            return false;
        }
        if (reason == PaymentReconciliationReasons.CodeMissing)
        {
            return false;
        }
        if (reason == PaymentReconciliationReasons.OrderNotFound)
        {
            return false;
        }
        return true;
    }

    /// <summary>Giao dịch tiền ra không phải sự cố; mọi lý do khác đều mở sự cố cho Admin (E7).</summary>
    public static bool ShouldRaiseIncident(string reason)
    {
        return reason != PaymentReconciliationReasons.NotIncoming;
    }

    /// <summary>Cắt chuỗi cho vừa độ dài cột; rỗng hoặc toàn khoảng trắng → null.</summary>
    public static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (value.Length <= maxLength)
        {
            return value;
        }
        return value.Substring(0, maxLength);
    }
}
