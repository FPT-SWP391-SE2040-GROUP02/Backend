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
        string ownerPhone = PhoneText(notice.OwnerPhone);
        string executorPhone = PhoneText(notice.ExecutorPhone);
        EmailParts defaults = Parts(
            notice.OwnerName, notice.OwnerEmail, ownerPhone, notice.ExecutorName, notice.ExecutorEmail, executorPhone);

        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = notice.FullName;
        tokens["TEN_CHU_KHO"] = notice.OwnerName;
        tokens["EMAIL_CHU_KHO"] = notice.OwnerEmail;
        tokens["SDT_CHU_KHO"] = ownerPhone;
        tokens["TEN_NGUOI_THUC_THI"] = notice.ExecutorName ?? string.Empty;
        tokens["EMAIL_NGUOI_THUC_THI"] = notice.ExecutorEmail ?? string.Empty;
        tokens["SDT_NGUOI_THUC_THI"] = executorPhone;
        EmailParts parts = EmailTemplateOverrides.Apply(EmailTemplateCatalog.PlanActivated, defaults, tokens);

        string text = $"Xin chào {notice.FullName},\n\n{parts.Title}\n\n{parts.Body}\n\n{parts.ButtonText}: {landingUrl}\n\n{parts.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: parts.Title,
            recipientName: notice.FullName,
            leadText: parts.Body,
            actionUrl: landingUrl,
            actionButtonText: parts.ButtonText,
            securityNotice: parts.Note);
        return new EmailContent(parts.Subject, text, html, EmailTemplateCatalog.PlanActivated);
    }

    /// <summary>Chữ gốc với biến {TEN_...} giữ nguyên, để Admin sao chép làm chỗ bắt đầu.</summary>
    public static EmailParts SampleParts()
    {
        return Parts("{TEN_CHU_KHO}", "{EMAIL_CHU_KHO}", "{SDT_CHU_KHO}", "{TEN_NGUOI_THUC_THI}", "{EMAIL_NGUOI_THUC_THI}", "{SDT_NGUOI_THUC_THI}");
    }

    private static string PhoneText(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return MissingPhone;
        }
        return phone;
    }

    private static EmailParts Parts(
        string ownerName, string? ownerEmail, string ownerPhone,
        string? executorName, string? executorEmail, string executorPhone)
    {
        List<string> lines = new List<string>();
        lines.Add(ownerName + " đã chọn bạn làm người nhận trên LegacyVault và vừa kích hoạt kế hoạch bàn giao.");
        lines.Add("Chủ kho: " + Contact(ownerName, ownerEmail, ownerPhone) + ".");
        if (!string.IsNullOrWhiteSpace(executorName))
        {
            lines.Add("Người thực thi: " + Contact(executorName, executorEmail, executorPhone) + ".");
        }
        lines.Add("Bạn chưa cần làm gì lúc này. Khi đến lúc bàn giao, bạn sẽ nhận một email khác kèm liên kết riêng để tiếp nhận.");
        return new EmailParts(
            Subject,
            Title,
            string.Join("\n", lines),
            "Tìm hiểu về LegacyVault",
            "Email này chỉ để thông báo và không chứa nội dung tài sản. Nếu bạn không quen biết người gửi, hãy bỏ qua email này.");
    }

    private static string Contact(string? name, string? email, string phoneText)
    {
        return name + ", email " + email + ", số điện thoại " + phoneText;
    }
}
