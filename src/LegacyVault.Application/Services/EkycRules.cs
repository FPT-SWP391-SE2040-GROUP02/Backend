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
        bool readIdUsable = reading.IdNumber.Length == 12 && IsAllDigits(reading.IdNumber);
        if (!readIdUsable)
        {
            // MATCH-04: OCR không chắc chắn thì không kết luận.
            return EkycIdentityMatch.NeedsReview;
        }

        if (!string.Equals(reading.IdNumber, expected.CitizenId, StringComparison.Ordinal))
        {
            // MATCH-05 chỉ áp dụng khi khác "rõ ràng"; OCR đang có cảnh báo thì có thể chỉ là đọc sai.
            if (reading.Warnings.Count > 0)
            {
                return EkycIdentityMatch.NeedsReview;
            }
            return EkycIdentityMatch.WrongPerson;
        }

        // CCCD trùng: tên hoặc ngày sinh lệch có thể do Owner nhập sai (MATCH-06), để Executor xem.
        if (NormalizeName(reading.FullName) != NormalizeName(expected.FullName))
        {
            return EkycIdentityMatch.NeedsReview;
        }
        DateOnly readBirth;
        if (!DateOnly.TryParseExact(reading.DateOfBirth, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out readBirth))
        {
            return EkycIdentityMatch.NeedsReview;
        }
        if (readBirth != expected.DateOfBirth)
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
