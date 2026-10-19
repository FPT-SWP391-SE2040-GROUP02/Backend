using System.Globalization;
using System.Net;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Dựng email báo Owner rằng có hồ sơ chứng tử (DEATH-08). Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// OPS-04, SEC-07: hàm không nhận CCCD, tên Executor hay nội dung giấy chứng tử nên không thể lộ ra.
/// Giờ hiển thị theo giờ Việt Nam (UTC+7, TIME-01). Tên được mã hóa HTML trong bản HTML.
/// </remarks>
public static class DeathCaseNoticeEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static EmailContent Build(string fullName, string caseCode, DateTimeOffset expectedObjectionEndsAt, string link)
    {
        string deadline = expectedObjectionEndsAt.ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);

        string subject = "LegacyVault: có yêu cầu xác nhận tình trạng của bạn";
        string paragraph = $"Một người được bạn chỉ định đã gửi hồ sơ báo tử cho kho của bạn (mã hồ sơ {caseCode}). "
            + $"Nếu bạn vẫn khỏe mạnh, hãy đăng nhập trước {deadline} và chọn \"Tôi còn sống\" để dừng hồ sơ.";
        const string note = "Nếu bạn không đăng nhập trước hạn trên, hồ sơ sẽ được xét duyệt theo quy trình. "
            + "Email này không chứa nội dung kho của bạn.";

        string text = $"Xin chào {fullName},\n\n{paragraph}\n\nĐăng nhập: {link}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Yêu cầu xác nhận tình trạng của bạn",
            fullName,
            paragraph,
            link,
            "Xác nhận tình trạng ngay",
            note);
        return new EmailContent(subject, text, html);
    }
}
