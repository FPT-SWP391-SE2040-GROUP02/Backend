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

    /// <summary>Số chiều vector đặc trưng của SFace; vector khác số chiều này bị xem là phản hồi lạ của dịch vụ.</summary>
    public const int FaceEmbeddingLength = 128;

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

    /// <summary>Lấy 4 số cuối của số CCCD; rỗng nếu không đủ 4 chữ số ở cuối.</summary>
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

    /// <summary>
    /// Độ tương đồng cosine giữa hai vector đặc trưng khuôn mặt, cùng công thức với SFace (FR_COSINE) của dịch vụ eKYC
    /// nên dùng chung ngưỡng MatchThreshold. Khác số chiều hoặc một vector toàn 0 thì trả -1 (không bao giờ khớp).
    /// </summary>
    public static double CosineSimilarity(float[] first, float[] second)
    {
        if (first.Length == 0 || first.Length != second.Length)
        {
            return -1;
        }

        double dot = 0;
        double firstNorm = 0;
        double secondNorm = 0;
        for (int index = 0; index < first.Length; index++)
        {
            dot += (double)first[index] * second[index];
            firstNorm += (double)first[index] * first[index];
            secondNorm += (double)second[index] * second[index];
        }
        if (firstNorm == 0 || secondNorm == 0)
        {
            return -1;
        }
        return dot / (Math.Sqrt(firstNorm) * Math.Sqrt(secondNorm));
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
    /// Mặt sau có cùng thẻ với mặt trước không (EKYC-04). Mặt trước chỉ lưu 4 số cuối nên chỉ so 4 số cuối
    /// của số căn cước đọc từ MRZ mặt sau. Thiếu một bên thì không coi là cùng thẻ.
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
    /// So giấy tờ vừa đọc với người được chỉ định trong snapshot (MATCH-01..05, MATCH-07).
    /// Chỉ dùng CCCD, họ tên, ngày sinh; email và số điện thoại không tham gia.
    /// </summary>
    public static EkycIdentityMatch CompareIdentity(EkycDocumentReading reading, BeneficiaryPersonalData expected)
    {
        return CompareIdentity(reading, expected.CitizenId, expected.FullName, expected.DateOfBirth);
    }

    /// <summary>
    /// So giấy tờ vừa đọc với danh tính đã gắn của Owner (EKYC-24). Cùng luật với người nhận: Matched / NeedsReview
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
            // MATCH-04: OCR không chắc chắn thì không kết luận.
            return EkycIdentityMatch.NeedsReview;
        }

        if (!string.Equals(reading.IdNumber, expectedCitizenId, StringComparison.Ordinal))
        {
            // MATCH-05 chỉ áp dụng khi khác "rõ ràng"; OCR đang có cảnh báo thì có thể chỉ là đọc sai.
            if (reading.Warnings.Count > 0)
            {
                return EkycIdentityMatch.NeedsReview;
            }
            return EkycIdentityMatch.WrongPerson;
        }

        // CCCD trùng: tên hoặc ngày sinh lệch có thể do Owner nhập sai (MATCH-06), để Executor xem.
        if (NormalizeName(reading.FullName) != NormalizeName(expectedFullName))
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

    /// <summary>Chuẩn hóa Unicode NFC, bỏ khoảng trắng thừa, không phân biệt hoa thường; KHÔNG bỏ dấu (MATCH-02).</summary>
    public static string NormalizeName(string name)
    {
        string composed = name.Normalize(NormalizationForm.FormC);
        string[] parts = composed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts).ToUpperInvariant();
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
