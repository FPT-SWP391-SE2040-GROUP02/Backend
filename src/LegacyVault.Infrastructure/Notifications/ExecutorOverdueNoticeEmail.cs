using System.Net;
using LegacyVault.Application.Notifications;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Dựng email báo Executor rằng Chủ kho đã quá hạn điểm danh và kết thúc thời gian chờ (DMS-05).
/// Hàm thuần, không gửi gì; sử dụng mẫu chuẩn LegacyVaultEmailTheme (giống CheckInReminderEmail của Owner).
/// </summary>
/// <remarks>
/// OPS-04, SEC-07: Email không chứa CCCD, tên thật hay danh mục tài sản của Owner để bảo vệ quyền riêng tư.
/// Tên người nhận được mã hóa HTML trong bản HTML.
/// </remarks>
public static class ExecutorOverdueNoticeEmail
{
    public static EmailContent Build(string fullName, string link, bool isReminder = false)
    {
        string key = KeyFor(isReminder);
        string kicker = isReminder ? "Nhắc lại thông báo" : "Thông báo Người thực hiện";

        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = fullName;
        EmailParts parts = EmailTemplateOverrides.Apply(key, DefaultParts(isReminder), tokens);

        string text = $"Xin chào {fullName},\n\n{parts.Body}\n\n{parts.ButtonText}: {link}\n\n{parts.Note}\n";

        string html = LegacyVaultEmailTheme.BuildHtml(
            title: parts.Title,
            recipientName: fullName,
            leadText: parts.Body,
            actionUrl: link,
            actionButtonText: parts.ButtonText,
            securityNotice: parts.Note,
            kicker: kicker);

        return new EmailContent(parts.Subject, text, html, key);
    }

    public static string KeyFor(bool isReminder)
    {
        return isReminder ? EmailTemplateCatalog.ExecutorOverdueReminder : EmailTemplateCatalog.ExecutorOverdue;
    }

    /// <summary>Chữ gốc, dùng làm chỗ bắt đầu cho Admin.</summary>
    public static EmailParts DefaultParts(bool isReminder)
    {
        string subject = isReminder
            ? "LegacyVault: Nhắc lại — Chủ kho quá hạn điểm danh, cần bạn kiểm tra"
            : "LegacyVault: chủ kho quá hạn điểm danh, cần bạn kiểm tra";

        string paragraph = (isReminder ? "Nhắc lại: " : string.Empty)
            + "Chủ một kho mà bạn là người thực hiện đã quá hạn điểm danh và hết thời gian chờ. "
            + "Vui lòng liên hệ kiểm tra tình trạng của chủ kho. Nếu chủ kho vẫn ổn, bạn có thể điểm danh thay; "
            + "nếu chủ kho đã mất, hãy mở hồ sơ báo tin trên trang người thực hiện.\n\n"
            + "Lưu ý quan trọng: Thông báo này không tự động mở kho và không bắt đầu bàn giao di sản.";

        return new EmailParts(
            subject,
            "Chủ kho quá hạn điểm danh — Cần bạn kiểm tra",
            paragraph,
            "Mở trang người thực hiện",
            "Lưu ý: Để bảo vệ quyền riêng tư, email này không chứa thông tin tài sản hay CCCD của chủ kho.");
    }
}
