using System.Globalization;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Dựng thư mốc lưu dữ liệu. Thư không có tên gói hay tài sản.</summary>
public static class RetentionNoticeEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Soạn thư nhắc mốc lưu dữ liệu gồm tiêu đề, bản chữ và bản HTML. Nội dung nói rõ hạn xóa theo giờ Việt Nam.</summary>
    public static EmailContent Build(RetentionNotice notice, string link)
    {
        string deadline = notice.DeleteAt.ToOffset(VietnamOffset).ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
        string subject = "LegacyVault: dữ liệu bàn giao cho bạn sắp được xóa";
        if (notice.Stage == RetentionRules.NoticeStarted)
        {
            subject = "LegacyVault: bắt đầu thời gian lưu dữ liệu bàn giao cho bạn";
        }
        string paragraph = $"Phần di sản số được chỉ định cho bạn trên LegacyVault sẽ bị xóa vĩnh viễn lúc {deadline} "
            + "nếu bạn chưa hoàn tất việc nhận. Hãy đăng nhập và làm theo hướng dẫn trước thời điểm này.";
        const string note = "Email này không chứa nội dung tài sản. Nếu bạn không biết về việc này, hãy bỏ qua.";

        string text = $"Xin chào {notice.FullName},\n\n{paragraph}\n\nĐăng nhập: {link}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            subject,
            notice.FullName,
            paragraph,
            link,
            "Đăng nhập nhận dữ liệu",
            note,
            "Mốc lưu giữ dữ liệu");
        return new EmailContent(subject, text, html);
    }
}
