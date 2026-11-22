using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng thư báo người nhận rằng họ đã được chọn, gửi khi Chủ kho kích hoạt kế hoạch. Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// Cùng khung với thư "Thông báo nhận bàn giao di sản số" (HandoverInvitationEmail) nhưng bỏ nhãn nhỏ phía trên tiêu đề.
/// Thư chỉ để thông báo: nút dẫn về trang giới thiệu, không có link riêng hay mã nhận.
/// Có thông tin liên hệ của Chủ kho và Người thực thi; không có tên gói hay tài sản.
/// </remarks>
public static class PlanActivatedNoticeEmail
{
    public const string Subject = "Bạn đã trở thành người nhận trên LegacyVault";

    public const string Title = "Bạn đã trở thành người nhận trên hệ thống của chúng tôi";

    private const string MissingPhone = "chưa cung cấp";

    /// <summary>Dựng thư báo kích hoạt kế hoạch hoàn chỉnh: ghép bản chữ và bản HTML.</summary>
    public static EmailContent Build(PlanActivatedNotice notice, string landingUrl)
    {
        EmailParts parts = Parts(notice);

        string text = $"Xin chào {notice.FullName},\n\n{parts.Title}\n\n{parts.Body}\n\n{parts.ButtonText}: {landingUrl}\n\n{parts.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: parts.Title,
            recipientName: notice.FullName,
            leadText: parts.Body,
            actionUrl: landingUrl,
            actionButtonText: parts.ButtonText,
            securityNotice: parts.Note);
        return new EmailContent(parts.Subject, text, html);
    }

    /// <summary>Số điện thoại để in trong thư; chưa có thì in chữ "chưa cung cấp".</summary>
    private static string PhoneText(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return MissingPhone;
        }
        return phone;
    }

    /// <summary>Chữ mặc định của thư báo kích hoạt kế hoạch: giới thiệu Chủ kho, và Người thực thi nếu có.</summary>
    private static EmailParts Parts(PlanActivatedNotice notice)
    {
        List<string> lines = new List<string>();
        lines.Add(notice.OwnerName + " đã chọn bạn làm người nhận trên LegacyVault và vừa kích hoạt kế hoạch bàn giao.");
        lines.Add("Chủ kho: " + Contact(notice.OwnerName, notice.OwnerEmail, PhoneText(notice.OwnerPhone)) + ".");
        if (!string.IsNullOrWhiteSpace(notice.ExecutorName))
        {
            lines.Add("Người thực thi: " + Contact(notice.ExecutorName, notice.ExecutorEmail, PhoneText(notice.ExecutorPhone)) + ".");
        }
        lines.Add("Bạn chưa cần làm gì lúc này. Khi đến lúc bàn giao, bạn sẽ nhận một email khác kèm liên kết riêng để tiếp nhận.");
        return new EmailParts(
            Subject,
            Title,
            string.Join("\n", lines),
            "Tìm hiểu về LegacyVault",
            "Email này chỉ để thông báo và không chứa nội dung tài sản. Nếu bạn không quen biết người gửi, hãy bỏ qua email này.");
    }

    /// <summary>Một dòng liên hệ gồm họ tên, email và số điện thoại.</summary>
    private static string Contact(string? name, string? email, string phoneText)
    {
        return name + ", email " + email + ", số điện thoại " + phoneText;
    }
}
