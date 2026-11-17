using System.Globalization;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Dựng nội dung email nhắc điểm danh (DMS-06) cho từng mốc. Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// OPS-04: chỉ có tên, mốc thời gian và liên kết tới trang điểm danh; tuyệt đối không có nội dung tài sản.
/// Giờ hiển thị theo giờ Việt Nam (UTC+7, TIME-01). Tên người dùng được mã hóa HTML trong bản HTML.
/// </remarks>
public static class CheckInReminderEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static EmailContent Build(
        CheckInReminderKind kind, string fullName, DateTimeOffset dueAt, DateTimeOffset graceEndsAt, string checkInUrl)
    {
        string due = FormatVietnamTime(dueAt);
        string graceEnds = FormatVietnamTime(graceEndsAt);
        (string subject, string paragraph) = DefaultText(kind, due, graceEnds);
        const string buttonText = "Điểm danh ngay";
        const string note =
            "Lưu ý: đăng nhập hoặc thanh toán không được tính là điểm danh. Email này không chứa nội dung tài sản của bạn.";

        string text = $"Xin chào {fullName},\n\n{paragraph}\n\n{buttonText}: {checkInUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            subject,
            fullName,
            paragraph,
            checkInUrl,
            buttonText,
            note,
            "Nhắc điểm danh");
        return new EmailContent(subject, text, html);
    }

    /// <summary>Chữ của từng mốc; due và graceEnds là chữ đã định dạng.</summary>
    private static (string Subject, string Paragraph) DefaultText(CheckInReminderKind kind, string due, string graceEnds)
    {
        string subject;
        string paragraph;
        switch (kind)
        {
            case CheckInReminderKind.DueInSevenDays:
            case CheckInReminderKind.DueInOneDay:
            case CheckInReminderKind.DueReminder3:
            case CheckInReminderKind.DueReminder4:
            case CheckInReminderKind.DueReminder5:
                // Số ngày do Admin đặt cho từng thư (CheckIn.DueReminderCount và các khóa ...ReminderDaysBeforeDue).
                int days = CheckInRules.DueReminderDaysAt(CheckInTimeline.DueReminderPosition(kind));
                if (days == 1)
                {
                    subject = "Ngày mai là hạn điểm danh LegacyVault";
                    paragraph = $"Hạn điểm danh của bạn là {due}, chỉ còn khoảng một ngày. Hãy đăng nhập và bấm \"Điểm danh\".";
                }
                else if (CheckInTimeline.DueReminderPosition(kind) == 1)
                {
                    subject = $"Còn {days} ngày nữa là tới hạn điểm danh LegacyVault";
                    paragraph = $"Hạn điểm danh tiếp theo của bạn là {due}. Hãy đăng nhập và bấm \"Điểm danh\" trước thời điểm này.";
                }
                else
                {
                    subject = $"Còn {days} ngày nữa là tới hạn điểm danh LegacyVault";
                    paragraph = $"Hạn điểm danh của bạn là {due}, chỉ còn khoảng {days} ngày. Hãy đăng nhập và bấm \"Điểm danh\".";
                }
                break;
            case CheckInReminderKind.GraceStarted:
                subject = "Bạn đã quá hạn điểm danh LegacyVault";
                paragraph = $"Hạn điểm danh ({due}) đã qua và bạn đang trong thời gian chờ, kéo dài tới {graceEnds}. "
                    + "Hãy điểm danh ngay. Nếu hết thời gian chờ mà chưa điểm danh, hệ thống sẽ nhờ người thực hiện (Executor) "
                    + "liên hệ kiểm tra tình hình của bạn. Việc này không tự mở kho và không bắt đầu bàn giao.";
                break;
            case CheckInReminderKind.GraceTwoDaysLeft:
                subject = "Thời gian chờ điểm danh LegacyVault còn 2 ngày";
                paragraph = $"Thời gian chờ sẽ hết lúc {graceEnds}. Hãy điểm danh trước thời điểm này để không phải nhờ người thực hiện (Executor) liên hệ kiểm tra.";
                break;
            default:
                int late = CheckInTimeline.LateReminderIndex(kind);
                if (late == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mốc nhắc không hợp lệ.");
                }
                subject = late == 1
                    ? "Bạn đã quá hạn điểm danh LegacyVault"
                    : $"Nhắc lần {late}: bạn vẫn chưa điểm danh LegacyVault";
                paragraph = $"Hạn điểm danh ({due}) đã qua. Đây là thư nhắc trễ lần {late}. Hãy điểm danh ngay. "
                    + $"Nếu tới {graceEnds} mà chưa điểm danh, hệ thống sẽ báo cho Người thực thi do bạn chọn để liên hệ "
                    + "kiểm tra tình hình của bạn. Việc này không tự mở kho và không bắt đầu bàn giao.";
                break;
        }

        return (subject, paragraph);
    }

    private static string FormatVietnamTime(DateTimeOffset value)
    {
        return value.ToOffset(VietnamOffset).ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
    }
}
