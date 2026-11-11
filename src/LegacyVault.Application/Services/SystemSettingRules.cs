using System.Globalization;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật kiểm tra giá trị các tham số Admin đặt ở trang Cấu hình (SystemSettings): số nguyên trong khoảng hợp lý,
/// true/false, độ dài chữ, và vài cặp tham số phải đi cùng nhau (thư nhắc thứ nhất xa hạn hơn thư thứ hai...).
/// Hàm thuần, không đọc DB. Các khóa Plans.* đã có bộ kiểm riêng (bảng giá) nên không nằm ở đây.
/// </summary>
/// <remarks>
/// Khoảng giá trị khớp với cách hệ thống đọc tham số (clamp trong CheckInRules, ProxyCheckInRules...) để số Admin
/// thấy trên màn hình đúng bằng số hệ thống chạy, không bị điều chỉnh âm thầm khi đọc.
/// </remarks>
public static class SystemSettingRules
{
    private sealed class IntegerRule
    {
        public string Label = string.Empty;
        public long Min;
        public long Max;
    }

    private sealed class TextRule
    {
        public string Label = string.Empty;
        public int MaxLength;
        public bool IsEmail;
        public bool IsPhone;
    }

    public const string FirstReminderKey = "CheckIn.FirstReminderDaysBeforeDue";
    public const string SecondReminderKey = "CheckIn.SecondReminderDaysBeforeDue";
    public const string IncidentReminderKey = "Incident.ReminderAfterHours";
    public const string IncidentEscalateKey = "Incident.EscalateAfterHours";
    public const string ExecutorExpiresKey = "Executor.InvitationExpiresDays";
    public const string ExecutorReminderKey = "Executor.ReminderAfterHours";

    private static readonly Dictionary<string, IntegerRule> IntegerRules = BuildIntegerRules();
    private static readonly Dictionary<string, TextRule> TextRules = BuildTextRules();
    private static readonly HashSet<string> BooleanKeys = new HashSet<string>
    {
        "Features.MaintenanceMode",
        "Time.DemoSeconds",
        AuthClock.FollowDemoSettingKey,
    };

    /// <summary>Giá trị mặc định của các khóa nằm trong cặp phải đi cùng nhau, dùng khi chưa có dòng hoặc dòng bị tắt.</summary>
    private static readonly Dictionary<string, long> RelationDefaults = new Dictionary<string, long>
    {
        { FirstReminderKey, 7 },
        { SecondReminderKey, 1 },
        { IncidentReminderKey, 48 },
        { IncidentEscalateKey, 72 },
        { ExecutorExpiresKey, 7 },
        { ExecutorReminderKey, 48 },
    };

    /// <summary>Các khóa tham gia kiểm tra theo cặp; service nạp giá trị hiện có của chúng từ DB.</summary>
    public static IReadOnlyCollection<string> RelationKeys
    {
        get { return RelationDefaults.Keys; }
    }

    public static long RelationDefault(string key)
    {
        return RelationDefaults[key];
    }

    private static Dictionary<string, IntegerRule> BuildIntegerRules()
    {
        Dictionary<string, IntegerRule> rules = new Dictionary<string, IntegerRule>();
        Add(rules, "CheckIn.DefaultCycleDays", "Chu kỳ điểm danh mặc định (ngày)", 7, 365);
        Add(rules, FirstReminderKey, "Thư nhắc trước hạn thứ nhất (ngày trước hạn)", 2, 60);
        Add(rules, SecondReminderKey, "Thư nhắc trước hạn thứ hai (ngày trước hạn)", 1, 30);
        Add(rules, "CheckIn.LateReminderGapDays", "Khoảng cách giữa hai thư nhắc trễ (ngày)", 1, 30);
        Add(rules, "CheckIn.MaxLateReminders", "Số thư nhắc trễ", 1, 5);
        Add(rules, "CheckIn.ExecutorProxyLimit", "Số lần Người thực thi điểm danh thay liên tiếp", 1, 20);

        Add(rules, "Handover.ResponseWindowDays", "Hạn người nhận phản hồi lời mời (ngày)", 1, 60);
        Add(rules, "Handover.DecisionWindowDays", "Hạn người nhận quyết định (ngày)", 1, 60);
        Add(rules, "Handover.GrantWindowDays", "Thời hạn được xem và tải tài sản (ngày)", 1, 365);
        Add(rules, "Handover.RetentionWindowDays", "Thời hạn lưu dữ liệu trước khi xóa (ngày)", 1, 730);
        Add(rules, "Handover.ResendCooldownMinutes", "Khoảng cách giữa hai lần gửi lại lời mời (phút)", 1, 1440);

        Add(rules, ExecutorExpiresKey, "Hạn lời mời Người thực thi (ngày)", 1, 60);
        Add(rules, ExecutorReminderKey, "Giờ nhắc sau khi mời Người thực thi (giờ)", 1, 1440);

        Add(rules, "DeathCase.SupplementDueDays", "Hạn bổ sung hồ sơ chứng tử (ngày)", 1, 60);
        Add(rules, "DeathCase.MaxFilesPerVersion", "Số tệp tối đa mỗi bản hồ sơ chứng tử", 1, 50);
        Add(rules, "DeathCase.MaxFileSizeMb", "Dung lượng tối đa một tệp hồ sơ chứng tử (MiB)", 1, 200);
        Add(rules, "DeathCase.ReviewDueHours", "Hạn Người xét duyệt hoàn tất thẩm định (giờ)", 1, 720);

        Add(rules, IncidentReminderKey, "Giờ nhắc người phụ trách sự cố (giờ)", 1, 720);
        Add(rules, IncidentEscalateKey, "Giờ báo sự cố lên Quản trị viên (giờ)", 1, 720);
        Add(rules, "Incident.MaxReviewIntervalDays", "Khoảng cách tối đa giữa các lần hẹn xem lại sự cố (ngày)", 1, 60);

        Add(rules, "Limits.MaxUploadsPerHour", "Số lần tải lên tối đa mỗi giờ", 1, 10000);
        Add(rules, "Limits.MaxAssetsPerVault", "Số tài sản tối đa trong một kho", 1, 100000);
        Add(rules, "Limits.MaxUploadBytesMb", "Dung lượng tối đa mỗi lần tải lên (MiB)", 1, 2048);
        Add(rules, "Limits.ReservationHoldMinutes", "Thời gian giữ chỗ khi tải lên (phút)", 1, 1440);

        Add(rules, "Security.MaxPinAttempts", "Số lần nhập sai mã PIN tối đa", 1, 20);
        Add(rules, "Security.PinLockoutMinutes", "Thời gian khóa PIN khi nhập sai (phút)", 1, 1440);
        return rules;
    }

    private static Dictionary<string, TextRule> BuildTextRules()
    {
        Dictionary<string, TextRule> rules = new Dictionary<string, TextRule>();
        AddText(rules, "Company.Name", "Tên công ty", 100, false);
        AddText(rules, "Company.Website", "Website công ty", 200, false);
        AddText(rules, "Company.Email", "Email công ty", 200, true);
        AddText(rules, "Company.Phone", "Số điện thoại công ty", 50, false, true);
        AddText(rules, "Company.Address", "Địa chỉ công ty", 300, false);
        return rules;
    }

    private static void Add(Dictionary<string, IntegerRule> rules, string key, string label, long min, long max)
    {
        IntegerRule rule = new IntegerRule();
        rule.Label = label;
        rule.Min = min;
        rule.Max = max;
        rules[key] = rule;
    }

    private static void AddText(
        Dictionary<string, TextRule> rules, string key, string label, int maxLength, bool isEmail, bool isPhone = false)
    {
        TextRule rule = new TextRule();
        rule.Label = label;
        rule.MaxLength = maxLength;
        rule.IsEmail = isEmail;
        rule.IsPhone = isPhone;
        rules[key] = rule;
    }

    /// <summary>
    /// Lỗi của một giá trị đơn lẻ (sai dạng, ngoài khoảng, quá dài), hoặc null khi hợp lệ hay khóa không có luật ở đây.
    /// Thông báo viết sẵn tiếng Việt để hiện thẳng cho Admin.
    /// </summary>
    public static string? FindValueError(string key, string value)
    {
        string text = value.Trim();
        IntegerRule? integerRule;
        if (IntegerRules.TryGetValue(key, out integerRule))
        {
            long number;
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                return integerRule.Label + " phải là số nguyên không âm.";
            }
            if (number < integerRule.Min || number > integerRule.Max)
            {
                return integerRule.Label + " phải từ " + integerRule.Min.ToString(CultureInfo.InvariantCulture)
                    + " đến " + integerRule.Max.ToString(CultureInfo.InvariantCulture) + ".";
            }
            return null;
        }
        if (BooleanKeys.Contains(key))
        {
            bool flag;
            if (!bool.TryParse(text, out flag))
            {
                return "Giá trị của '" + key + "' phải là true hoặc false.";
            }
            return null;
        }
        TextRule? textRule;
        if (TextRules.TryGetValue(key, out textRule))
        {
            if (text.Length > textRule.MaxLength)
            {
                return textRule.Label + " tối đa " + textRule.MaxLength.ToString(CultureInfo.InvariantCulture) + " ký tự.";
            }
            if (textRule.IsEmail && text.Length > 0 && (!text.Contains('@') || text.Contains(' ')))
            {
                return textRule.Label + " không đúng dạng email.";
            }
            if (textRule.IsPhone && text.Length > 0 && !IsValidPhone(text))
            {
                return textRule.Label + " không hợp lệ: chỉ gồm số, dấu cách, dấu chấm, gạch ngang, ngoặc, có thể bắt đầu bằng + hoặc (, từ 8 đến 15 chữ số.";
            }
        }
        return null;
    }

    /// <summary>
    /// Số điện thoại hoặc tổng đài: bắt đầu bằng số, dấu + hoặc dấu (, chỉ gồm số, dấu cách, dấu chấm, gạch ngang, ngoặc, và có từ 8 đến 15 chữ số
    /// (ví dụ "1900 0000", "0901 234 567", "+84 901 234 567").
    /// </summary>
    public static bool IsValidPhone(string text)
    {
        if (text.Length == 0 || !(char.IsAsciiDigit(text[0]) || text[0] == '+' || text[0] == '('))
        {
            return false;
        }
        int digits = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c == '+' && i == 0)
            {
                continue;
            }
            else if (c != ' ' && c != '.' && c != '-' && c != '(' && c != ')')
            {
                return false;
            }
        }
        return digits >= 8 && digits <= 15;
    }

    /// <summary>
    /// Lỗi của các cặp tham số phải đi cùng nhau, tính trên giá trị sau khi lưu (effective: khóa → số). Khóa thiếu thì dùng mặc định.
    /// </summary>
    public static string? FindRelationError(IReadOnlyDictionary<string, long> effective)
    {
        long first = Pick(effective, FirstReminderKey);
        long second = Pick(effective, SecondReminderKey);
        if (first <= second)
        {
            return "Thư nhắc trước hạn thứ nhất phải xa hạn hơn thư thứ hai (số ngày lớn hơn). Hiện là "
                + first.ToString(CultureInfo.InvariantCulture) + " ngày và " + second.ToString(CultureInfo.InvariantCulture) + " ngày.";
        }

        long reminder = Pick(effective, IncidentReminderKey);
        long escalate = Pick(effective, IncidentEscalateKey);
        if (escalate <= reminder)
        {
            return "Giờ báo sự cố lên Quản trị viên phải sau giờ nhắc người phụ trách. Hiện là "
                + escalate.ToString(CultureInfo.InvariantCulture) + " giờ và " + reminder.ToString(CultureInfo.InvariantCulture) + " giờ.";
        }

        long expiresDays = Pick(effective, ExecutorExpiresKey);
        long remindHours = Pick(effective, ExecutorReminderKey);
        if (remindHours >= expiresDays * 24)
        {
            return "Giờ nhắc lời mời Người thực thi phải sớm hơn hạn của lời mời, nếu không thư nhắc không bao giờ được gửi. Hiện nhắc sau "
                + remindHours.ToString(CultureInfo.InvariantCulture) + " giờ, lời mời hết hạn sau "
                + (expiresDays * 24).ToString(CultureInfo.InvariantCulture) + " giờ.";
        }
        return null;
    }

    private static long Pick(IReadOnlyDictionary<string, long> effective, string key)
    {
        long value;
        if (effective.TryGetValue(key, out value))
        {
            return value;
        }
        return RelationDefaults[key];
    }
}
