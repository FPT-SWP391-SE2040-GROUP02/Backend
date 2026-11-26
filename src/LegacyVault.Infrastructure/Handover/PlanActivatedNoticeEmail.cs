using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng thư báo người nhận rằng họ đã được chọn, gửi khi Chủ tài sản kích hoạt kế hoạch. Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// Cùng khung với thư "Thông báo nhận bàn giao di sản số" (HandoverInvitationEmail) nhưng bỏ nhãn nhỏ phía trên tiêu đề.
/// Hệ thống không tự biết Chủ tài sản còn hay mất, nên thư phải nói rõ: khi việc đó xảy ra, người nhận tự gửi yêu cầu.
/// Có link riêng (RequestToken) thì nút dẫn tới trang gửi yêu cầu; không có thì dẫn về trang chủ và chỉ vào mục
/// "Nhận tài sản". Thư không bao giờ có mã nhận.
/// Có thông tin liên hệ của Chủ tài sản và Người thực thi; không có tên gói hay tài sản.
/// </remarks>
public static class PlanActivatedNoticeEmail
{
    public const string Subject = "Bạn đã trở thành người nhận trên LegacyVault";

    public const string Title = "Bạn đã trở thành người nhận trên hệ thống của chúng tôi";

    public const string RequestButtonText = "Gửi yêu cầu nhận tài sản";

    public const string OpenButtonText = "Mở LegacyVault";

    private const string MissingPhone = "chưa cung cấp";

    /// <summary>Dựng thư báo kích hoạt kế hoạch hoàn chỉnh: ghép bản chữ và bản HTML.</summary>
    public static EmailContent Build(PlanActivatedNotice notice, string actionUrl)
    {
        EmailParts parts = Parts(notice);

        string text = $"Xin chào {notice.FullName},\n\n{parts.Title}\n\n{parts.Body}\n\n{parts.ButtonText}: {actionUrl}\n\n{parts.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: parts.Title,
            recipientName: notice.FullName,
            leadText: parts.Body,
            actionUrl: actionUrl,
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

    /// <summary>
    /// Chữ mặc định của thư báo kích hoạt kế hoạch: liên hệ Chủ tài sản và Người thực thi, rồi việc người nhận phải làm khi
    /// Chủ tài sản qua đời.
    /// </summary>
    private static EmailParts Parts(PlanActivatedNotice notice)
    {
        List<string> lines = new List<string>();
        lines.Add(notice.OwnerName + " đã chọn bạn làm người nhận trên LegacyVault và vừa kích hoạt kế hoạch bàn giao.");
        lines.Add("Chủ tài sản: " + Contact(notice.OwnerName, notice.OwnerEmail, PhoneText(notice.OwnerPhone)) + ".");
        if (!string.IsNullOrWhiteSpace(notice.ExecutorName))
        {
            lines.Add("Người thực thi: " + Contact(notice.ExecutorName, notice.ExecutorEmail, PhoneText(notice.ExecutorPhone)) + ".");
        }
        lines.Add("Hiện tại bạn chưa cần làm gì. Hãy giữ lại thư này.");

        string buttonText;
        if (notice.RequestToken.Length > 0)
        {
            lines.Add("Khi Chủ tài sản qua đời, bạn cần chủ động gửi yêu cầu nhận tài sản, kèm di chúc, bằng nút bên dưới. "
                + "LegacyVault không tự biết khi nào việc đó xảy ra nên sẽ không tự nhắc bạn.");
            buttonText = RequestButtonText;
        }
        else
        {
            lines.Add("Khi Chủ tài sản qua đời, bạn cần chủ động đăng nhập bằng đúng email này, mở mục Nhận tài sản và gửi "
                + "yêu cầu kèm di chúc. LegacyVault không tự biết khi nào việc đó xảy ra nên sẽ không tự nhắc bạn.");
            buttonText = OpenButtonText;
        }
        lines.Add("Sau khi bạn gửi, hệ thống hướng dẫn từng bước: luật sư xét giấy tờ, bạn nộp ảnh căn cước, ký biên bản "
            + "rồi nhập mã nhận để tải tài sản.");
        return new EmailParts(
            Subject,
            Title,
            string.Join("\n", lines),
            buttonText,
            "Email này không chứa nội dung tài sản và dành riêng cho bạn, vui lòng không chuyển tiếp. Nếu nút trong thư "
                + "không còn dùng được, hãy đăng nhập bằng đúng email này và mở mục Nhận tài sản. Nếu bạn không quen biết "
                + "người gửi, hãy bỏ qua email này.");
    }

    /// <summary>Một dòng liên hệ gồm họ tên, email và số điện thoại.</summary>
    private static string Contact(string? name, string? email, string phoneText)
    {
        return name + ", email " + email + ", số điện thoại " + phoneText;
    }
}
