using System.Text;
using LegacyVault.Application.Exceptions;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật thuần của hồ sơ chứng tử (SRS §6.2 F2B), dùng chung cho service và job.</summary>
public static class DeathCaseRules
{
    /// <summary>ASSIGN-01: Verifier có 48 giờ để xét hồ sơ được giao.</summary>
    public static int ReviewDueHours => RuntimeSettings.Int("DeathCase.ReviewDueHours", 48);

    /// <summary>NFR-09: tối đa 10 tệp giấy chứng tử mỗi phiên bản.</summary>
    public static int MaxFilesPerVersion => RuntimeSettings.Int("DeathCase.MaxFilesPerVersion", 10);

    /// <summary>NFR-09: mỗi tệp tối đa 20 MiB.</summary>
    public static long MaxFileBytes => RuntimeSettings.Int("DeathCase.MaxFileSizeMb", 20) * 1024L * 1024;

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
    /// Các phần còn thiếu để gửi được: chỉ cần ít nhất một giấy chứng tử hoặc trích lục khai tử (mã FILES).
    /// Họ tên, số giấy, ngày mất, ngày cấp, nơi cấp không còn do Executor nhập; Người xét duyệt đọc trên giấy.
    /// Số CCCD của Chủ kho lấy từ danh tính đã xác minh.
    /// </summary>
    public static List<string> GetMissingRequirements(int certificateFileCount)
    {
        List<string> missing = new List<string>();
        if (certificateFileCount <= 0)
        {
            missing.Add(MissingFiles);
        }
        return missing;
    }

    /// <summary>Số tệp loại giấy chứng tử / trích lục khai tử trong một bản hồ sơ.</summary>
    public static int CountCertificateFiles(IEnumerable<DeathCaseFile> files)
    {
        int count = 0;
        foreach (DeathCaseFile file in files)
        {
            if (file.Kind == DeathCaseFileKind.DeathCertificate)
            {
                count++;
            }
        }
        return count;
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

    /// <summary>
    /// Mã hồ sơ rút gọn trong thư: 8 ký tự hex CUỐI của Id, chữ hoa. Không lấy phần đầu vì Guid v7 bắt đầu bằng
    /// mốc thời gian, các hồ sơ tạo gần nhau sẽ trùng mã. Dùng chung cho các thư nhắc Verifier và thư báo Executor.
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
