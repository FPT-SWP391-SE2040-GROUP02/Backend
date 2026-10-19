using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    /// E6: dùng trường code của SePay nếu có (không rỗng, dài tối đa 16); không thì tìm mẫu LV + 8 ký tự thuộc bảng
    /// BillingRules.PaymentCodeAlphabet trong nội dung chuyển khoản (không phân biệt hoa thường). Trả chữ HOA; không có → null.
    /// </summary>
    public static string? FindPaymentCode(string? code, string? content)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            string trimmedCode = code.Trim();
            if (trimmedCode.Length <= MaxPaymentCodeLength)
            {
                return trimmedCode.ToUpperInvariant();
            }
        }

        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        string upperContent = content.ToUpperInvariant();
        int codeLength = BillingRules.PaymentCodePrefix.Length + BillingRules.PaymentCodeRandomLength;
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
