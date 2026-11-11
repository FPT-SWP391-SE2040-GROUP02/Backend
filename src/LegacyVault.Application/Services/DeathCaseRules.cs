using System.Globalization;
using System.Text;
using LegacyVault.Application.Exceptions;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật thuần của hồ sơ chứng tử (SRS §6.2 F2B), dùng chung cho service và job.</summary>
public static class DeathCaseRules
{
    /// <summary>DEATH-08: Owner có 72 giờ phản đối, tính từ lúc thông báo được giao.</summary>
    public static int ObjectionHours => RuntimeSettings.Int("DeathCase.ObjectionHours", 72);

    /// <summary>ASSIGN-01: Verifier có 48 giờ để xét hồ sơ được giao.</summary>
    public static int ReviewDueHours => RuntimeSettings.Int("DeathCase.ReviewDueHours", 48);

    /// <summary>NFR-09: tối đa 10 tệp giấy chứng tử mỗi phiên bản.</summary>
    public static int MaxFilesPerVersion => RuntimeSettings.Int("DeathCase.MaxFilesPerVersion", 10);

    /// <summary>NFR-09: mỗi tệp tối đa 20 MiB.</summary>
    public static long MaxFileBytes => RuntimeSettings.Int("DeathCase.MaxFileSizeMb", 20) * 1024L * 1024;

    /// <summary>D3: thử gửi thông báo tối đa 5 lần rồi đánh dấu Failed.</summary>
    public static int MaxNoticeAttempts => RuntimeSettings.Int("DeathCase.MaxNoticeAttempts", 5);

    /// <summary>Số ký tự của Id hồ sơ dùng làm mã rút gọn trong thư.</summary>
    public const int CaseCodeLength = 8;

    public const string MissingOwnerFullName = "OWNER_FULL_NAME";
    public const string MissingOwnerCitizenId = "OWNER_CITIZEN_ID";
    public const string MissingDateOfDeath = "DATE_OF_DEATH";
    public const string MissingCertificateNumber = "CERTIFICATE_NUMBER";
    public const string MissingIssuedOn = "ISSUED_ON";
    public const string MissingIssuedBy = "ISSUED_BY";
    public const string MissingFiles = "FILES";
    public const string MissingDeathRegistrationPlace = "DEATH_REGISTRATION_PLACE";

    /// <summary>D7: mã cảnh báo Người xét duyệt khi chủ kho còn điểm danh sau ngày mất ghi trên giấy.</summary>
    public const string WarningDeathBeforeLastCheckIn = "DEATH_BEFORE_LAST_CHECKIN";

    /// <summary>D7: mã cảnh báo khi đăng ký khai tử trễ hơn số ngày cho phép so với ngày mất.</summary>
    public const string WarningLateRegistration = "LATE_REGISTRATION";

    /// <summary>D7: đăng ký khai tử sau ngày mất quá số ngày này thì cảnh báo (không chặn duyệt).</summary>
    public const int LateRegistrationDays = 15;

    /// <summary>
    /// DEATH-01: mọi mã phần hồ sơ có thể thiếu. Verifier chỉ được yêu cầu bổ sung các mã này.
    /// Mã mới luôn thêm CUỐI mảng; GetMissingRequirements trả theo thứ tự riêng của nó.
    /// </summary>
    public static readonly string[] AllRequirementCodes =
    {
        MissingOwnerFullName, MissingOwnerCitizenId, MissingDateOfDeath,
        MissingCertificateNumber, MissingIssuedOn, MissingIssuedBy, MissingFiles,
        MissingDeathRegistrationPlace,
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
    /// DEATH-01: các phần còn thiếu để gửi được. Executor phải điền 3 thông tin trên giấy chứng tử (ngày mất, ngày đăng ký
    /// khai tử, nơi đăng ký khai tử gồm tỉnh và phường/xã) và tải ít nhất một giấy chứng tử hoặc trích lục khai tử (FILES).
    /// Họ tên và ngày sinh của Chủ kho lấy từ danh tính đã xác minh nên Executor không phải nhập.
    /// Chỉ xét có hay chưa; đúng/sai của giá trị do ValidateCertificateInfo kiểm.
    /// </summary>
    public static List<string> GetMissingRequirements(
        DateOnly? dateOfDeath, DateOnly? issuedOn, string? provinceCode, string? wardCode, int certificateFileCount)
    {
        List<string> missing = new List<string>();
        if (dateOfDeath == null)
        {
            missing.Add(MissingDateOfDeath);
        }
        if (issuedOn == null)
        {
            missing.Add(MissingIssuedOn);
        }
        if (string.IsNullOrWhiteSpace(provinceCode) || string.IsNullOrWhiteSpace(wardCode))
        {
            missing.Add(MissingDeathRegistrationPlace);
        }
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
    /// DEATH-01: luật kiểm 3 thông tin trên giấy chứng tử, dùng chung cho sửa nháp và gửi hồ sơ. Thiếu giá trị (null) thì bỏ qua
    /// (bản nháp chưa nhập, việc thiếu do GetMissingRequirements báo). "Hôm nay" là ngày Việt Nam của utcNow
    /// (TimeProvider, đúng cả khi tua thời gian). planActivatedAt là lúc Chủ kho kích hoạt kế hoạch; null thì bỏ qua luật đó.
    /// Lỗi 400 VALIDATION_ERROR theo tên trường: dateOfDeath, issuedOn, deathRegistrationProvinceCode, deathRegistrationWardCode.
    /// </summary>
    public static void ValidateCertificateInfo(
        DateOnly? dateOfDeath,
        DateOnly? issuedOn,
        string? provinceCode,
        string? wardCode,
        DateTimeOffset? planActivatedAt,
        DateTimeOffset utcNow,
        IAdministrativeUnitCatalog catalog)
    {
        DateOnly today = TodayInVietnam(utcNow);
        if (dateOfDeath != null)
        {
            if (dateOfDeath.Value > today)
            {
                throw ValidationErrors.For("dateOfDeath", "Ngày mất không được sau hôm nay.");
            }
            if (planActivatedAt != null)
            {
                DateOnly activatedDay = TodayInVietnam(planActivatedAt.Value);
                if (dateOfDeath.Value < activatedDay)
                {
                    string activatedText = activatedDay.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                    throw ValidationErrors.For("dateOfDeath",
                        "Ngày mất phải sau ngày Chủ kho kích hoạt kế hoạch (" + activatedText + ").");
                }
            }
        }
        if (issuedOn != null)
        {
            if (issuedOn.Value > today)
            {
                throw ValidationErrors.For("issuedOn", "Ngày đăng ký khai tử không được sau hôm nay.");
            }
            if (dateOfDeath != null && issuedOn.Value < dateOfDeath.Value)
            {
                throw ValidationErrors.For("issuedOn", "Ngày đăng ký khai tử phải từ ngày mất trở đi.");
            }
        }

        bool hasProvince = !string.IsNullOrWhiteSpace(provinceCode);
        bool hasWard = !string.IsNullOrWhiteSpace(wardCode);
        if (hasProvince && catalog.FindProvince(provinceCode!) == null)
        {
            throw ValidationErrors.For("deathRegistrationProvinceCode", "Tỉnh/thành phố không hợp lệ.");
        }
        if (hasWard)
        {
            // Phường/xã phải thuộc đúng tỉnh đã chọn; chưa chọn tỉnh thì cũng coi là không thuộc.
            bool wardBelongs = false;
            if (hasProvince && catalog.FindWard(provinceCode!, wardCode!) != null)
            {
                wardBelongs = true;
            }
            if (!wardBelongs)
            {
                throw ValidationErrors.For("deathRegistrationWardCode", "Phường/xã không thuộc tỉnh/thành phố đã chọn.");
            }
        }
    }

    /// <summary>
    /// D7: ngày Việt Nam của lần điểm danh cuối của Chủ kho. Chủ kho còn điểm danh SAU ngày mất ghi trên giấy
    /// (lần điểm danh cuối &gt; ngày mất) là dấu hiệu bất thường. Thiếu ngày mất hoặc chưa từng điểm danh thì không cảnh báo.
    /// </summary>
    public static bool IsDeathBeforeLastCheckIn(DateOnly? dateOfDeath, DateTimeOffset? lastCheckInAt)
    {
        if (dateOfDeath == null || lastCheckInAt == null)
        {
            return false;
        }
        DateOnly lastCheckInDay = TodayInVietnam(lastCheckInAt.Value);
        return lastCheckInDay > dateOfDeath.Value;
    }

    /// <summary>D7: đăng ký khai tử cách ngày mất hơn 15 ngày. Thiếu một trong hai ngày thì không cảnh báo.</summary>
    public static bool IsLateRegistration(DateOnly? dateOfDeath, DateOnly? issuedOn)
    {
        if (dateOfDeath == null || issuedOn == null)
        {
            return false;
        }
        int days = issuedOn.Value.DayNumber - dateOfDeath.Value.DayNumber;
        return days > LateRegistrationDays;
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
