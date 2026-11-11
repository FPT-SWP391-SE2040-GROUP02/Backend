using System.Globalization;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần của nút "Tua thời gian" (DEMO): chọn giờ đích, kiểm ngày, tính các mốc điểm danh.
/// Không đọc DB hay đồng hồ; các mốc điểm danh dùng lại CheckInTimeline nên khớp đúng với job nền.
/// </summary>
public static class DemoClockRules
{
    /// <summary>Mỗi lần chỉ tua tối đa chừng này ngày để một request không chạy quá lâu.</summary>
    public const int MaxTravelDays = 400;

    /// <summary>Chặn vòng lặp vô hạn nếu việc nền cứ sinh thêm mốc mới.</summary>
    public const int MaxSteps = 600;

    /// <summary>Giờ trong ngày (giờ Việt Nam) mà hệ thống đặt khi tua tới một ngày.</summary>
    public const int TargetHourVietnam = 9;

    /// <summary>Việt Nam không đổi giờ theo mùa nên dùng độ lệch cố định +07:00.</summary>
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Đặt giờ hệ thống ngay sau mốc một chút để điều kiện "đã tới mốc" (>=) chắc chắn đúng.</summary>
    public static readonly TimeSpan AfterMilestone = TimeSpan.FromSeconds(1);

    public const string KindCheckInReminder = "CHECK_IN_REMINDER";
    public const string KindCheckInDue = "CHECK_IN_DUE";
    public const string KindCheckInGraceEnd = "CHECK_IN_GRACE_END";
    public const string KindTargetDay = "TARGET_DAY";

    public static bool TryParseDate(string text, out DateOnly date)
    {
        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>9:00 sáng giờ Việt Nam của ngày chọn, đổi sang UTC.</summary>
    public static DateTimeOffset TargetInstant(DateOnly date)
    {
        DateTimeOffset local = new DateTimeOffset(
            date.Year, date.Month, date.Day, TargetHourVietnam, 0, 0, VietnamOffset);
        return local.ToUniversalTime();
    }

    /// <summary>Chỉ tua tới, tối đa MaxTravelDays ngày. Trả thông báo lỗi dễ hiểu, hoặc null nếu hợp lệ.</summary>
    public static string? FindTravelError(DateTimeOffset now, DateTimeOffset target)
    {
        if (target <= now)
        {
            return "Ngày tua phải sau ngày hiện tại của hệ thống. Chỉ tua tới được, không tua lùi.";
        }
        if (target - now > TimeSpan.FromDays(MaxTravelDays))
        {
            return "Mỗi lần chỉ tua tối đa " + MaxTravelDays.ToString(CultureInfo.InvariantCulture) + " ngày.";
        }
        return null;
    }

    /// <summary>
    /// Các mốc điểm danh nằm trong khoảng (after, until): nhắc trước hạn, nhắc trễ, đến hạn và hết thời gian chờ
    /// (lúc báo Người thực thi). Công thức lấy từ CheckInTimeline.
    /// </summary>
    public static List<DemoDeadline> CheckInMilestones(
        List<DemoOpenPeriod> periods, DateTimeOffset after, DateTimeOffset until)
    {
        List<DemoDeadline> result = new List<DemoDeadline>();
        foreach (DemoOpenPeriod period in periods)
        {
            IReadOnlyList<int> dueDays = CheckInRules.DueReminderDays;
            foreach (CheckInReminderKind kind in CheckInTimeline.ActiveKinds(dueDays.Count, period.MaxLateReminders))
            {
                DateTimeOffset at = CheckInTimeline.MilestoneAt(kind, period.DueAt, period.LateReminderGapDays, dueDays);
                if (at == period.DueAt)
                {
                    // Thư nhắc trễ đầu tiên gửi đúng lúc đến hạn: gộp vào mốc "đến hạn" để một lần dừng chỉ có một bước.
                    continue;
                }
                AddIfInRange(result, at, KindCheckInReminder, after, until);
            }
            AddIfInRange(result, period.DueAt, KindCheckInDue, after, until);
            AddIfInRange(result, period.GraceEndsAt, KindCheckInGraceEnd, after, until);
        }
        return result;
    }

    /// <summary>Mốc sớm nhất trong danh sách; danh sách rỗng thì null.</summary>
    public static DemoDeadline? PickEarliest(List<DemoDeadline> candidates)
    {
        DemoDeadline? earliest = null;
        foreach (DemoDeadline candidate in candidates)
        {
            if (earliest == null || candidate.At < earliest.At)
            {
                earliest = candidate;
            }
        }
        return earliest;
    }

    private static void AddIfInRange(
        List<DemoDeadline> list, DateTimeOffset at, string kind, DateTimeOffset after, DateTimeOffset until)
    {
        if (at > after && at < until)
        {
            DemoDeadline deadline = new DemoDeadline();
            deadline.At = at;
            deadline.Kind = kind;
            list.Add(deadline);
        }
    }
}
