using System.Globalization;
using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật eKYC: ngưỡng, hạn mã tham chiếu, giới hạn ảnh. Không truy cập dữ liệu.</summary>
public static class EkycRules
{
    /// <summary>Ngưỡng cosine của SFace dùng để coi hai khuôn mặt là một người. Server cố định, FE không được đổi.</summary>
    public const double MatchThreshold = 0.363;

    /// <summary>Ảnh tối đa 8 MiB, bằng giới hạn của dịch vụ eKYC.</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>Dịch vụ eKYC giữ khuôn mặt trên giấy tờ 30 phút.</summary>
    public static readonly TimeSpan DocumentTokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Phiên bản thông báo xin đồng ý hiện hành (EKYC-03). Đổi nội dung thông báo thì đổi chuỗi này.</summary>
    public const string NoticeVersion = "2026-10-v1";

    /// <summary>Sau khi đồng ý, người dùng phải bắt đầu chụp trong thời gian này.</summary>
    public static readonly TimeSpan ConsentLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Các dạng ngày sinh chấp nhận khi đọc từ dịch vụ eKYC: ISO (dạng thật của dịch vụ) và ngày/tháng/năm.</summary>
    private static readonly string[] BirthDateFormats = new string[] { "yyyy-MM-dd", "dd/MM/yyyy" };

    /// <summary>Chỉ nhận ảnh JPEG hoặc PNG.</summary>
    public static bool IsAllowedContentType(string contentType)
    {
        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    /// <summary>Còn mã tham chiếu dùng được để so khớp selfie hay không.</summary>
    public static bool CanMatchFace(EkycVerification verification, DateTimeOffset now)
    {
        if (verification.Status != EkycStatus.Pending)
        {
            return false;
        }
        if (verification.DocumentToken == null || verification.DocumentTokenExpiresAt == null)
        {
            return false;
        }
        if (now >= verification.DocumentTokenExpiresAt.Value)
        {
            return false;
        }
        return true;
    }

    /// <summary>Số CCCD đầy đủ đọc từ giấy tờ (bỏ khoảng trắng); rỗng nếu không phải toàn chữ số hoặc dài quá 12.</summary>
    public static string GetCitizenId(string idNumber)
    {
        string trimmed = idNumber.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 12)
        {
            return string.Empty;
        }
        foreach (char character in trimmed)
        {
            if (!char.IsAsciiDigit(character))
            {
                return string.Empty;
            }
        }
        return trimmed;
    }

    /// <summary>Lấy 4 số cuối của số CCCD (chỉ dùng nội bộ để so mặt trước, mặt sau); rỗng nếu không đủ 4 chữ số ở cuối.</summary>
    public static string GetCitizenIdLast4(string idNumber)
    {
        if (idNumber.Length < 4)
        {
            return string.Empty;
        }
        string last4 = idNumber.Substring(idNumber.Length - 4);
        foreach (char character in last4)
        {
            if (!char.IsAsciiDigit(character))
            {
                return string.Empty;
            }
        }
        return last4;
    }

    /// <summary>Lần đồng ý còn dùng được: đúng phiên bản thông báo hiện hành và chưa quá hạn (EKYC-03).</summary>
    public static bool IsConsentValid(EkycConsent? consent, DateTimeOffset now)
    {
        if (consent == null)
        {
            return false;
        }
        if (consent.NoticeVersion != NoticeVersion)
        {
            return false;
        }
        if (now >= consent.ConsentedAt + ConsentLifetime)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Mặt sau có cùng thẻ với mặt trước không (EKYC-04). MRZ mặt sau có thể đọc thiếu đầu số nên chỉ so 4 số cuối
    /// của hai mặt. Thiếu một bên thì không coi là cùng thẻ.
    /// </summary>
    public static bool IsSameCard(string backIdNumber, string frontLast4)
    {
        if (frontLast4.Length != 4)
        {
            return false;
        }
        string backLast4 = GetCitizenIdLast4(backIdNumber);
        if (backLast4.Length != 4)
        {
            return false;
        }
        return string.Equals(backLast4, frontLast4, StringComparison.Ordinal);
    }

    /// <summary>
    /// So giấy tờ vừa đọc với danh tính đã gắn của Owner (EKYC-24). Kết quả: Matched / NeedsReview
    /// (OCR không chắc, hoặc CCCD trùng mà tên/ngày sinh lệch) / WrongPerson (CCCD khác rõ ràng).
    /// </summary>
    public static EkycIdentityMatch CompareIdentity(EkycDocumentReading reading, OwnerIdentityData expected)
    {
        return CompareIdentity(reading, expected.CitizenId, expected.FullName, expected.DateOfBirth);
    }

    /// <summary>
    /// Lấy danh tính đọc được để gắn cho Owner (EKYC-27). Phải có đủ CCCD 12 số, họ tên và ngày sinh hợp lệ;
    /// thiếu một thứ thì trả null (OCR chưa chắc: yêu cầu chụp lại, không ai duyệt thay Owner).
    /// </summary>
    public static OwnerIdentityData? TryReadOwnerIdentity(EkycDocumentReading reading)
    {
        if (reading.IdNumber.Length != 12 || !IsAllDigits(reading.IdNumber))
        {
            return null;
        }
        string fullName = NormalizeName(reading.FullName);
        if (fullName.Length == 0)
        {
            return null;
        }
        DateOnly dateOfBirth;
        if (!DateOnly.TryParseExact(reading.DateOfBirth, BirthDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dateOfBirth))
        {
            return null;
        }

        OwnerIdentityData data = new OwnerIdentityData();
        data.CitizenId = reading.IdNumber;
        data.FullName = fullName;
        data.DateOfBirth = dateOfBirth;
        return data;
    }

    private static EkycIdentityMatch CompareIdentity(
        EkycDocumentReading reading, string expectedCitizenId, string expectedFullName, DateOnly expectedDateOfBirth)
    {
        bool readIdUsable = reading.IdNumber.Length == 12 && IsAllDigits(reading.IdNumber);
        if (!readIdUsable)
        {
            // OCR không chắc chắn thì không kết luận.
            return EkycIdentityMatch.NeedsReview;
        }

        if (!string.Equals(reading.IdNumber, expectedCitizenId, StringComparison.Ordinal))
        {
            // Chỉ coi là sai người khi khác "rõ ràng"; OCR đang có cảnh báo thì có thể chỉ là đọc sai.
            if (reading.Warnings.Count > 0)
            {
                return EkycIdentityMatch.NeedsReview;
            }
            return EkycIdentityMatch.WrongPerson;
        }

        // CCCD trùng: tên hoặc ngày sinh lệch thì chưa đủ chắc, yêu cầu chụp lại.
        if (NormalizeNameForCompare(reading.FullName) != NormalizeNameForCompare(expectedFullName))
        {
            return EkycIdentityMatch.NeedsReview;
        }
        // Dịch vụ eKYC trả ngày sinh dạng ISO yyyy-MM-dd (logic.py: birthday.isoformat()); giữ dd/MM/yyyy để chịu được bản khác.
        DateOnly readBirth;
        if (!DateOnly.TryParseExact(reading.DateOfBirth, BirthDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out readBirth))
        {
            return EkycIdentityMatch.NeedsReview;
        }
        if (readBirth != expectedDateOfBirth)
        {
            return EkycIdentityMatch.NeedsReview;
        }
        return EkycIdentityMatch.Matched;
    }

    /// <summary>Độ giống tên tối thiểu (0..1) giữa tên Owner khai và tên OCR đọc; chỉnh được qua Ekyc:NameSimilarityThreshold.</summary>
    public const double NameSimilarityThreshold = 0.85;

    /// <summary>
    /// Độ giống 0..1 của hai họ tên sau chuẩn hóa (hoa, bỏ dấu, gộp khoảng trắng): 1 - Levenshtein / độ dài lớn hơn.
    /// Dung sai cho lỗi OCR dấu tiếng Việt nhưng vẫn bắt được đổi sang tên khác.
    /// </summary>
    public static double NameSimilarity(string first, string second)
    {
        string a = NormalizeNameForCompare(first);
        string b = NormalizeNameForCompare(second);
        int longest = Math.Max(a.Length, b.Length);
        if (longest == 0)
        {
            return 1.0;
        }
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return 1.0 - (double)previous[b.Length] / longest;
    }

    /// <summary>
    /// So thông tin Owner khai với thông tin OCR đọc. Trả mã lệch ("NAME_MISMATCH", "DOB_MISMATCH") hoặc null nếu khớp.
    /// Ngày sinh so chính xác; họ tên so theo độ giống. Số CCCD không so vì không cho sửa.
    /// </summary>
    public static string? FindDeclaredMismatch(OwnerIdentityData read, string declaredName, DateOnly declaredBirth, double nameThreshold)
    {
        if (declaredBirth != read.DateOfBirth)
        {
            return "DOB_MISMATCH";
        }
        if (NameSimilarity(read.FullName, declaredName) < nameThreshold)
        {
            return "NAME_MISMATCH";
        }
        return null;
    }

    /// <summary>Chuẩn hóa Unicode NFC, bỏ khoảng trắng thừa, không phân biệt hoa thường; KHÔNG bỏ dấu (MATCH-02).</summary>
    public static string NormalizeName(string name)
    {
        string composed = name.Normalize(NormalizationForm.FormC);
        string[] parts = composed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts).ToUpperInvariant();
    }

    /// <summary>
    /// Tên dùng để so khớp: như <see cref="NormalizeName"/> nhưng bỏ dấu tiếng Việt và đổi Đ thành D, vì OCR hay đọc sai dấu
    /// (ví dụ Trịnh thành Trinh) và Chủ tài sản có thể khai tên không dấu. Chỉ để so sánh, không dùng để lưu.
    /// </summary>
    public static string NormalizeNameForCompare(string name)
    {
        string decomposed = NormalizeName(name).Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }
        return builder.ToString().Replace('Đ', 'D').Normalize(NormalizationForm.FormC);
    }

    private static bool IsAllDigits(string text)
    {
        foreach (char character in text)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }
        return true;
    }
}
