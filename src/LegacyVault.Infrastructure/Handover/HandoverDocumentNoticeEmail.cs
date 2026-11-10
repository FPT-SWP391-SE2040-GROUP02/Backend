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
        string subject;
        string title;
        string paragraph;
        string buttonText = "Mở mục Biên Bản";

        if (notice.Kind == HandoverDocumentNoticeKind.VerifierTurn)
        {
            subject = "Có biên bản đã ký cần bạn kiểm tra";
            title = "Biên bản chờ bạn kiểm tra thủ công";
            paragraph = "Biên bản (mã " + notice.DocumentCode + ") của người nhận " + Name(notice) + " đã được người nhận ký. "
                + "Bạn kiểm tra thủ công từng mục rồi gửi; bạn chịu trách nhiệm về kết quả.";
            buttonText = "Mở hồ sơ chờ duyệt";
        }
        else if (notice.Kind == HandoverDocumentNoticeKind.RecipientReturned)
        {
            subject = "Biên bản cần bạn bổ sung";
            title = "Biên bản được trả về cho bạn";
            paragraph = "Biên bản (mã " + notice.DocumentCode + ") cần bổ sung: " + notice.Reason + ". Bạn sửa phần của mình rồi ký lại.";
        }
        else
        {
            subject = "Biên bản đã hoàn tất";
            title = "Biên bản đã hoàn tất";
            paragraph = notice.DocumentKind == "ACCEPT"
                ? "Biên bản (mã " + notice.DocumentCode + ") đã hoàn tất, người nhận được mở tải tài sản."
                : "Văn bản từ chối (mã " + notice.DocumentCode + ") đã hoàn tất.";
        }

        const string note = "Email không chứa nội dung tài sản. Nếu không liên quan, bạn hãy bỏ qua.";
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{paragraph}\n\n{buttonText}: {appUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: paragraph,
            actionUrl: appUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(subject, text, html);
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
