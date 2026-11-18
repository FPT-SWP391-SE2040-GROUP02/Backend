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

    /// <summary>Dựng thư báo kích hoạt kế hoạch hoàn chỉnh: chữ gốc, thay bằng chữ Admin đã sửa (nếu có), rồi ghép bản chữ và bản HTML.</summary>
    public static EmailContent Build(PlanActivatedNotice notice, string landingUrl)
    {
        string ownerPhone = PhoneText(notice.OwnerPhone);
        string executorPhone = PhoneText(notice.ExecutorPhone);
        EmailParts defaults = Parts(notice);

        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = notice.FullName;
        tokens["TEN_CHU_KHO"] = notice.OwnerName;
        tokens["EMAIL_CHU_KHO"] = notice.OwnerEmail;
        tokens["SDT_CHU_KHO"] = ownerPhone;
        tokens["TEN_NGUOI_THUC_THI"] = string.Empty;
        tokens["EMAIL_NGUOI_THUC_THI"] = string.Empty;
        if (notice.ExecutorName != null)
        {
            tokens["TEN_NGUOI_THUC_THI"] = notice.ExecutorName;
        }
        if (notice.ExecutorEmail != null)
        {
            tokens["EMAIL_NGUOI_THUC_THI"] = notice.ExecutorEmail;
        }
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
        PlanActivatedNotice sample = new PlanActivatedNotice
        {
            OwnerName = "{TEN_CHU_KHO}",
            OwnerEmail = "{EMAIL_CHU_KHO}",
            OwnerPhone = "{SDT_CHU_KHO}",
            ExecutorName = "{TEN_NGUOI_THUC_THI}",
            ExecutorEmail = "{EMAIL_NGUOI_THUC_THI}",
            ExecutorPhone = "{SDT_NGUOI_THUC_THI}"
        };
        return Parts(sample);
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
