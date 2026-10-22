using System.Globalization;
using System.Net;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng email mời nhận tài sản (INVITE-01). Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// OPS-04: chỉ có tên người nhận, link và hạn phản hồi; hàm không nhận tên gói hay tên tài sản nên không thể lộ ra.
/// Giờ hiển thị theo giờ Việt Nam (UTC+7, TIME-01). Tên được mã hóa HTML trong bản HTML.
/// </remarks>
public static class HandoverInvitationEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static EmailContent Build(string fullName, string link, DateTimeOffset responseDeadline)
    {
        string deadline = responseDeadline.ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);

        string subject = "Bạn được mời nhận một phần di sản số trên LegacyVault";
        string paragraph = "Bạn được một người dùng LegacyVault chỉ định làm người nhận. "
            + $"Vui lòng mở liên kết dưới đây trước {deadline} để xác nhận. Bạn cần đăng nhập bằng tài khoản đã xác minh email.";
        const string note = "Nếu bạn không biết về lời mời này, hãy bỏ qua email. Email này không chứa nội dung tài sản.";

        string text = $"Xin chào {fullName},\n\n{paragraph}\n\nMở liên kết: {link}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Thông báo nhận bàn giao di sản số",
            fullName,
            paragraph,
            link,
            "Tiếp nhận bàn giao di sản",
            note,
            "Thông báo bàn giao");
        return new EmailContent(subject, text, html);
    }
}
