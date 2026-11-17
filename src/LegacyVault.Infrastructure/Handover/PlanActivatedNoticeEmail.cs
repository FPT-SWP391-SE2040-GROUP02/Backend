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

    public static EmailContent Build(PlanActivatedNotice notice, string landingUrl)
    {
        List<string> lines = new List<string>();
        lines.Add(notice.OwnerName + " đã chọn bạn làm người nhận trên LegacyVault và vừa kích hoạt kế hoạch bàn giao.");
        lines.Add("Chủ kho: " + Contact(notice.OwnerName, notice.OwnerEmail, notice.OwnerPhone) + ".");
        if (!string.IsNullOrWhiteSpace(notice.ExecutorName))
        {
            lines.Add("Người thực thi: " + Contact(notice.ExecutorName, notice.ExecutorEmail, notice.ExecutorPhone) + ".");
        }
        lines.Add("Bạn chưa cần làm gì lúc này. Khi đến lúc bàn giao, bạn sẽ nhận một email khác kèm liên kết riêng để tiếp nhận.");
        string paragraph = string.Join("\n", lines);

        const string note = "Email này chỉ để thông báo và không chứa nội dung tài sản. Nếu bạn không quen biết người gửi, hãy bỏ qua email này.";
        const string buttonText = "Tìm hiểu về LegacyVault";

        string text = $"Xin chào {notice.FullName},\n\n{Title}\n\n{paragraph}\n\n{buttonText}: {landingUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: Title,
            recipientName: notice.FullName,
            leadText: paragraph,
            actionUrl: landingUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(Subject, text, html);
    }

    private static string Contact(string? name, string? email, string? phone)
    {
        string phoneText = MissingPhone;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            phoneText = phone;
        }
        return name + ", email " + email + ", số điện thoại " + phoneText;
    }
}
