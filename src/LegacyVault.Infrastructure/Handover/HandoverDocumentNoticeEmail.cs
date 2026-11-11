using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng thư báo về biên bản đồng ý nhận hoặc văn bản từ chối. Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>Thư chỉ nêu loại biên bản, mã và việc cần làm; không có nội dung tài sản, số căn cước hay giấy chứng tử.</remarks>
public static class HandoverDocumentNoticeEmail
{
    public static EmailContent Build(HandoverDocumentNotice notice, string appUrl)
    {
        string key = KeyFor(notice.Kind);
        EmailParts defaults = Parts(notice.Kind, notice.DocumentCode, Name(notice), notice.Reason, notice.DocumentKind);

        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = notice.FullName;
        tokens["MA_BIEN_BAN"] = notice.DocumentCode;
        tokens["TEN_BEN_NHAN"] = Name(notice);
        tokens["LY_DO"] = notice.Reason ?? string.Empty;
        EmailParts parts = EmailTemplateOverrides.Apply(key, defaults, tokens);

        string text = $"Xin chào {notice.FullName},\n\n{parts.Title}\n\n{parts.Body}\n\n{parts.ButtonText}: {appUrl}\n\n{parts.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: parts.Title,
            recipientName: notice.FullName,
            leadText: parts.Body,
            actionUrl: appUrl,
            actionButtonText: parts.ButtonText,
            securityNotice: parts.Note);
        return new EmailContent(parts.Subject, text, html, key);
    }

    public static string KeyFor(HandoverDocumentNoticeKind kind)
    {
        if (kind == HandoverDocumentNoticeKind.VerifierTurn)
        {
            return EmailTemplateCatalog.DocVerifierTurn;
        }
        if (kind == HandoverDocumentNoticeKind.RecipientReturned)
        {
            return EmailTemplateCatalog.DocRecipientReturned;
        }
        return EmailTemplateCatalog.DocCompleted;
    }

    /// <summary>Chữ gốc của từng loại, biến {MA_BIEN_BAN}... giữ nguyên để Admin sao chép làm chỗ bắt đầu.</summary>
    public static EmailParts SampleParts(HandoverDocumentNoticeKind kind)
    {
        return Parts(kind, "{MA_BIEN_BAN}", "{TEN_BEN_NHAN}", "{LY_DO}", "ACCEPT");
    }

    private static EmailParts Parts(HandoverDocumentNoticeKind kind, string code, string name, string? reason, string documentKind)
    {
        string subject;
        string title;
        string paragraph;
        string buttonText = "Mở mục Biên Bản";

        if (kind == HandoverDocumentNoticeKind.VerifierTurn)
        {
            subject = "Có biên bản đã ký cần bạn kiểm tra";
            title = "Biên bản chờ bạn kiểm tra thủ công";
            paragraph = "Biên bản (mã " + code + ") của người nhận " + name + " đã được người nhận ký. "
                + "Bạn kiểm tra thủ công từng mục rồi gửi; bạn chịu trách nhiệm về kết quả.";
            buttonText = "Mở hồ sơ chờ duyệt";
        }
        else if (kind == HandoverDocumentNoticeKind.RecipientReturned)
        {
            subject = "Biên bản cần bạn bổ sung";
            title = "Biên bản được trả về cho bạn";
            paragraph = "Biên bản (mã " + code + ") cần bổ sung: " + reason + ". Bạn sửa phần của mình rồi ký lại.";
        }
        else
        {
            subject = "Biên bản đã hoàn tất";
            title = "Biên bản đã hoàn tất";
            paragraph = documentKind == "ACCEPT"
                ? "Biên bản (mã " + code + ") đã hoàn tất, người nhận được mở tải tài sản."
                : "Văn bản từ chối (mã " + code + ") đã hoàn tất.";
        }
        return new EmailParts(subject, title, paragraph, buttonText, "Email không chứa nội dung tài sản. Nếu không liên quan, bạn hãy bỏ qua.");
    }

    private static string Name(HandoverDocumentNotice notice)
    {
        if (notice.RecipientName.Length > 0)
        {
            return notice.RecipientName;
        }
        return "(chưa ghi tên)";
    }
}
