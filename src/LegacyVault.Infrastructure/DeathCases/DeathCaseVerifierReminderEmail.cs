using System.Globalization;
using System.Net;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Dựng email nhắc Verifier sắp quá hạn xét hồ sơ chứng tử (ASSIGN-04, TIME-02). Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// OPS-04, SEC-07: hàm không nhận CCCD, tên Owner, tên Executor hay nội dung giấy chứng tử nên không thể lộ ra.
/// Giờ hiển thị theo giờ Việt Nam (UTC+7, TIME-01). Tên được mã hóa HTML trong bản HTML.
/// </remarks>
public static class DeathCaseVerifierReminderEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static EmailContent Build(string fullName, string caseCode, DateTimeOffset dueAt, string link)
    {
        string deadline = dueAt.ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);

        string subject = "LegacyVault: hồ sơ chứng tử cần bạn xét trước hạn";
        string paragraph = $"Hồ sơ chứng tử mã {caseCode} đang chờ bạn ra quyết định. "
            + $"Vui lòng đăng nhập và xét trước {deadline}.";
        const string note = "Quá hạn hồ sơ sẽ được giao cho Verifier khác. "
            + "Email này không chứa nội dung hồ sơ.";

        string text = $"Xin chào {fullName},\n\n{paragraph}\n\nĐăng nhập: {link}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Hồ sơ chứng tử cần xét trước hạn",
            fullName,
            paragraph,
            link,
            "Xem hồ sơ thẩm định",
            note);
        return new EmailContent(subject, text, html);
    }
}
