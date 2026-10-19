using System.Globalization;
using System.Net;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Dựng email báo Executor rằng hồ sơ chứng tử cần bổ sung hoặc đã bị từ chối. Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// Hàm không nhận CCCD, tên Owner, lý do của Verifier hay nội dung giấy chứng tử nên không thể lộ ra.
/// Giờ hiển thị theo giờ Việt Nam (UTC+7). Tên được mã hóa HTML trong bản HTML.
/// </remarks>
public static class DeathCaseExecutorNoticeEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static EmailContent Build(
        string fullName, string caseCode, DeathCaseDecisionKind kind, DateTimeOffset? supplementDueAt, string link)
    {
        string subject;
        string paragraph;
        if (kind == DeathCaseDecisionKind.SupplementRequested)
        {
            subject = "LegacyVault: hồ sơ chứng tử cần bổ sung";
            paragraph = $"Verifier yêu cầu bổ sung hồ sơ chứng tử mã {caseCode}.";
            if (supplementDueAt != null)
            {
                string deadline = supplementDueAt.Value.ToOffset(VietnamOffset)
                    .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
                paragraph = paragraph + $" Vui lòng đăng nhập, xem phần cần bổ sung và nộp bản mới trước {deadline}.";
            }
            else
            {
                paragraph = paragraph + " Vui lòng đăng nhập để xem phần cần bổ sung và nộp bản mới.";
            }
        }
        else
        {
            subject = "LegacyVault: hồ sơ chứng tử đã bị từ chối";
            paragraph = $"Verifier đã từ chối hồ sơ chứng tử mã {caseCode}. "
                + "Vui lòng đăng nhập để xem lý do; khi có giấy tờ hợp lệ, bạn có thể mở hồ sơ mới.";
        }
        const string note = "Email này không chứa nội dung hồ sơ.";

        string text = $"Xin chào {fullName},\n\n{paragraph}\n\nĐăng nhập: {link}\n\n{note}\n";
        string html = $"<p>Xin chào {WebUtility.HtmlEncode(fullName)},</p>"
            + $"<p>{WebUtility.HtmlEncode(paragraph)}</p>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Mở trang Executor</a> ({WebUtility.HtmlEncode(link)})</p>"
            + $"<p><small>{WebUtility.HtmlEncode(note)}</small></p>";
        return new EmailContent(subject, text, html);
    }
}
