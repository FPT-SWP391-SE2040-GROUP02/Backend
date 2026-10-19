using System.Text;
using LegacyVault.Application.Exceptions;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật thuần của hồ sơ chứng tử (SRS §6.2 F2B), dùng chung cho service và job.</summary>
public static class DeathCaseRules
{
    /// <summary>DEATH-08: Owner có 72 giờ phản đối, tính từ lúc thông báo được giao.</summary>
    public const int ObjectionHours = 72;

    /// <summary>ASSIGN-01: Verifier có 48 giờ để xét hồ sơ được giao.</summary>
    public const int ReviewDueHours = 48;

    /// <summary>NFR-09: tối đa 10 tệp giấy chứng tử mỗi phiên bản.</summary>
    public const int MaxFilesPerVersion = 10;

    /// <summary>NFR-09: mỗi tệp tối đa 20 MiB.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>D3: thử gửi thông báo tối đa 5 lần rồi đánh dấu Failed.</summary>
    public const int MaxNoticeAttempts = 5;

    /// <summary>Số ký tự của Id hồ sơ dùng làm mã rút gọn trong thư.</summary>
    public const int CaseCodeLength = 8;

    public const string MissingOwnerFullName = "OWNER_FULL_NAME";
    public const string MissingOwnerCitizenId = "OWNER_CITIZEN_ID";
    public const string MissingDateOfDeath = "DATE_OF_DEATH";
    public const string MissingCertificateNumber = "CERTIFICATE_NUMBER";
    public const string MissingIssuedOn = "ISSUED_ON";
    public const string MissingIssuedBy = "ISSUED_BY";
    public const string MissingFiles = "FILES";

    /// <summary>
    /// DEATH-01: mọi mã phần hồ sơ có thể thiếu, đúng thứ tự GetMissingRequirements trả về.
    /// Verifier chỉ được yêu cầu bổ sung các mã này.
    /// </summary>
    public static readonly string[] AllRequirementCodes =
    {
        MissingOwnerFullName, MissingOwnerCitizenId, MissingDateOfDeath,
        MissingCertificateNumber, MissingIssuedOn, MissingIssuedBy, MissingFiles,
    };

    /// <summary>Ngày trên giấy tờ tính theo giờ Việt Nam (UTC+07:00).</summary>
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>DEATH-05: chỉ mở/gửi hồ sơ khi kế hoạch đang chạy (đã kích hoạt, chưa khóa).</summary>
    public static bool CanOpen(PlanStatus planStatus)
    {
        return PlanRules.IsRunning(planStatus);
    }

    /// <summary>DEATH-05, D1: hồ sơ còn chiếm suất "một hồ sơ mỗi kho" khi chưa bị từ chối (kể cả Draft).</summary>
    public static bool IsOpen(DeathCaseStatus status)
    {
        if (status == DeathCaseStatus.Rejected)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// DEATH-04: chỉ sửa được bản mới nhất chưa gửi, khi hồ sơ còn ở giai đoạn Executor được khai báo.
    /// Bản đã có SubmittedAt không bao giờ sửa.
    /// </summary>
    public static bool CanEditVersion(DeathCase deathCase, DeathCaseVersion version)
    {
        if (version.SubmittedAt != null)
        {
            return false;
        }
        if (version.VersionNo != deathCase.CurrentVersionNo)
        {
            return false;
        }
        if (deathCase.Status == DeathCaseStatus.Draft
            || deathCase.Status == DeathCaseStatus.UnderReview
            || deathCase.Status == DeathCaseStatus.SupplementRequired)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// D5, DEATH-04: tạo bản mới khi hồ sơ đang xét hoặc cần bổ sung và bản mới nhất đã gửi
    /// (mỗi hồ sơ chỉ có tối đa một bản nháp).
    /// </summary>
    public static bool CanCreateNewVersion(DeathCase deathCase, DeathCaseVersion currentVersion)
    {
        if (currentVersion.SubmittedAt == null)
        {
            return false;
        }
        if (deathCase.Status == DeathCaseStatus.UnderReview
            || deathCase.Status == DeathCaseStatus.SupplementRequired)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// DEATH-01: các phần còn thiếu để gửi được: đủ 6 trường và ít nhất một trang giấy.
    /// Chuỗi null hoặc chỉ khoảng trắng tính là thiếu.
    /// </summary>
    public static List<string> GetMissingRequirements(
        string? ownerFullName, string? ownerCitizenId, DateOnly? dateOfDeath,
        string? certificateNumber, DateOnly? issuedOn, string? issuedBy, int fileCount)
    {
        List<string> missing = new List<string>();
        if (TextInput.NullIfBlank(ownerFullName) == null)
        {
            missing.Add(MissingOwnerFullName);
        }
        if (TextInput.NullIfBlank(ownerCitizenId) == null)
        {
            missing.Add(MissingOwnerCitizenId);
        }
        if (dateOfDeath == null)
        {
            missing.Add(MissingDateOfDeath);
        }
        if (TextInput.NullIfBlank(certificateNumber) == null)
        {
            missing.Add(MissingCertificateNumber);
        }
        if (issuedOn == null)
        {
            missing.Add(MissingIssuedOn);
        }
        if (TextInput.NullIfBlank(issuedBy) == null)
        {
            missing.Add(MissingIssuedBy);
        }
        if (fileCount <= 0)
        {
            missing.Add(MissingFiles);
        }
        return missing;
    }

    /// <summary>
    /// DEATH-01: ngày mất không ở tương lai; ngày cấp không trước ngày mất và không ở tương lai.
    /// todayInVietnam là ngày hiện tại theo giờ Việt Nam. Trường null thì bỏ qua (bản nháp chưa nhập).
    /// </summary>
    public static void ValidateDates(DateOnly? dateOfDeath, DateOnly? issuedOn, DateOnly todayInVietnam)
    {
        if (dateOfDeath != null && dateOfDeath.Value > todayInVietnam)
        {
            throw ValidationErrors.For("dateOfDeath", "Ngày mất không được ở tương lai.");
        }
        if (issuedOn != null)
        {
            if (issuedOn.Value > todayInVietnam)
            {
                throw ValidationErrors.For("issuedOn", "Ngày cấp không được ở tương lai.");
            }
            if (dateOfDeath != null && issuedOn.Value < dateOfDeath.Value)
            {
                throw ValidationErrors.For("issuedOn", "Ngày cấp không được trước ngày mất.");
            }
        }
    }

    /// <summary>
    /// DEATH-11: thay đổi trọng yếu = đổi danh tính Owner (họ tên, CCCD) hoặc ngày mất.
    /// Họ tên so sau khi bỏ khoảng trắng thừa, không phân biệt hoa thường. Số giấy, nơi cấp, ngày cấp không tính.
    /// </summary>
    public static bool IsCriticalChange(
        string? oldOwnerFullName, string? oldOwnerCitizenId, DateOnly? oldDateOfDeath,
        string? newOwnerFullName, string? newOwnerCitizenId, DateOnly? newDateOfDeath)
    {
        string oldName = NormalizeNameForCompare(oldOwnerFullName);
        string newName = NormalizeNameForCompare(newOwnerFullName);
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.Equals(oldOwnerCitizenId, newOwnerCitizenId, StringComparison.Ordinal))
        {
            return true;
        }
        if (oldDateOfDeath != newDateOfDeath)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// DEATH-10, D7: điểm danh "sau ngày mất" là điểm danh từ 00:00 giờ Việt Nam của ngày kế tiếp ngày mất.
    /// Trả mốc đó theo UTC.
    /// </summary>
    /// <summary>
    /// DEATH-13: CCCD khai trên hồ sơ khác CCCD đã xác minh của Owner. Owner chưa xác minh (verifiedLast4 null)
    /// hoặc hồ sơ chưa có số thì không kết luận được, trả false.
    /// </summary>
    public static bool IsIdentityMismatch(string? verifiedLast4, string? caseCitizenId)
    {
        if (string.IsNullOrEmpty(verifiedLast4) || string.IsNullOrEmpty(caseCitizenId) || caseCitizenId.Length < 4)
        {
            return false;
        }
        string caseLast4 = caseCitizenId.Substring(caseCitizenId.Length - 4);
        return !string.Equals(caseLast4, verifiedLast4, StringComparison.Ordinal);
    }

    /// <summary>
    /// DEATH-13, EKYC-27: so ĐỦ 12 số CCCD khai trên hồ sơ với danh tính đã gắn của Owner. Hồ sơ chưa có số thì không
    /// kết luận được, trả false.
    /// </summary>
    public static bool IsIdentityMismatchFull(string boundCitizenId, string? caseCitizenId)
    {
        if (string.IsNullOrEmpty(caseCitizenId))
        {
            return false;
        }
        return !string.Equals(boundCitizenId, caseCitizenId, StringComparison.Ordinal);
    }

    public static DateTimeOffset CheckInCutoffUtc(DateOnly dateOfDeath)
    {
        DateTime nextDayStart = dateOfDeath.AddDays(1).ToDateTime(TimeOnly.MinValue);
        DateTimeOffset cutoffInVietnam = new DateTimeOffset(nextDayStart, VietnamOffset);
        return cutoffInVietnam.ToUniversalTime();
    }

    /// <summary>DEATH-08: hạn phản đối = lúc thông báo được giao + 72 giờ.</summary>
    public static DateTimeOffset ObjectionEndsAt(DateTimeOffset deliveredAt)
    {
        return deliveredAt.AddHours(ObjectionHours);
    }

    /// <summary>
    /// Mã hồ sơ rút gọn trong thư: 8 ký tự hex CUỐI của Id, chữ hoa. Không lấy phần đầu vì Guid v7 bắt đầu bằng
    /// mốc thời gian, các hồ sơ tạo gần nhau sẽ trùng mã. Dùng chung cho thư báo Owner và thư nhắc Verifier.
    /// </summary>
    public static string CaseCode(Guid caseId)
    {
        string hex = caseId.ToString("N");
        return hex.Substring(hex.Length - CaseCodeLength).ToUpperInvariant();
    }

    /// <summary>Ngày hiện tại theo giờ Việt Nam.</summary>
    public static DateOnly TodayInVietnam(DateTimeOffset utcNow)
    {
        DateTimeOffset nowInVietnam = utcNow.ToOffset(VietnamOffset);
        return DateOnly.FromDateTime(nowInVietnam.DateTime);
    }

    /// <summary>Bỏ khoảng trắng đầu/cuối, gộp nhiều khoảng trắng thành một, chuẩn Unicode NFC; null → "".</summary>
    private static string NormalizeNameForCompare(string? name)
    {
        if (name == null)
        {
            return string.Empty;
        }

        string composed = name.Normalize(NormalizationForm.FormC).Trim();
        StringBuilder builder = new StringBuilder();
        bool previousIsSpace = false;
        foreach (char c in composed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousIsSpace)
                {
                    builder.Append(' ');
                }
                previousIsSpace = true;
            }
            else
            {
                builder.Append(c);
                previousIsSpace = false;
            }
        }
        return builder.ToString();
    }
}
