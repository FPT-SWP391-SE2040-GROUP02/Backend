using System.Globalization;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng email mời nhận tài sản (INVITE-01). Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// - OPS-04: không có tên gói hay tên tài sản; chỉ có tên người nhận, tên Chủ kho, liên hệ Người thực thi, link và hạn phản hồi.
/// - Đây là thư thứ hai người nhận được: lần đầu họ đã được báo khi Chủ kho bật kế hoạch bàn giao, nên thư nhắc lại chuyện cũ.
/// - Không đoán giới tính từ tên: dùng "bạn" cho người nhận và chỉ dùng họ tên cho Chủ kho, Người thực thi.
/// - Giờ hiển thị theo giờ Việt Nam (UTC+7, TIME-01). Mọi chữ do người dùng nhập được mã hóa HTML trong bản HTML.
/// </remarks>
public static class HandoverInvitationEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Bản rút gọn khi chưa có thông tin liên hệ của kho (dữ liệu cũ, test).</summary>
    public static EmailContent Build(string fullName, string link, DateTimeOffset responseDeadline)
    {
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = fullName;
        invitation.ResponseDeadline = responseDeadline;
        return Build(invitation, link, null);
    }

    /// <param name="supportContact">Câu liên hệ hỗ trợ của nhóm, ví dụ "email ho-tro@... hoặc số 090...". Null thì chỉ bảo báo lại cho LegacyVault.</param>
    public static EmailContent Build(HandoverInvitation invitation, string link, string? supportContact)
    {
        return Build(invitation, link, supportContact, true);
    }

    /// <summary>Chữ gốc của tiêu đề, nút và ghi chú với biến giữ nguyên, để Admin sao chép làm chỗ bắt đầu.</summary>
    public static EmailParts SampleParts()
    {
        HandoverInvitation sample = new HandoverInvitation();
        sample.FullName = "{TEN_NGUOI_NHAN}";
        sample.OwnerFullName = "{TEN_CHU_KHO}";
        sample.ResponseDeadline = DateTimeOffset.UtcNow;
        EmailContent content = Build(sample, string.Empty, null, false);
        return content.Defaults!;
    }

    private static EmailContent Build(HandoverInvitation invitation, string link, string? supportContact, bool applyOverrides)
    {
        string deadline = invitation.ResponseDeadline.ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
        string shortDeadline = invitation.ResponseDeadline.ToOffset(VietnamOffset)
            .ToString("dd/MM", CultureInfo.InvariantCulture);

        string owner = invitation.OwnerFullName.Trim();
        string ownerLabel = owner;
        if (owner.Length == 0)
        {
            ownerLabel = "chủ kho";
        }
        string executor = invitation.ExecutorFullName.Trim();
        bool hasExecutor = executor.Length > 0;
        string executorLabel = executor;
        if (!hasExecutor)
        {
            executorLabel = "Người thực thi";
        }

        string subject = $"[Cần xử lý trước {shortDeadline}] Bạn có thể nhận tài sản số {ownerLabel} để lại";
        string title = $"Đã đến lúc bạn nhận phần tài sản {ownerLabel} để lại";

        List<string> lines = new List<string>();
        lines.Add($"Trước đây {ownerLabel} đã báo cho bạn biết bạn được chọn là người nhận một phần tài sản số của "
            + "họ trên LegacyVault. Nay đã đến lúc bạn nhận phần đó: người này đã qua đời nên không thể tự gửi lời nhắn này.");
        if (hasExecutor)
        {
            lines.Add($"Người được tin tưởng nhất là {executor}, người thực thi di chúc của {ownerLabel}, đã thay gửi giấy chứng tử. "
                + "Bên mình cũng đã kiểm tra và xác nhận đầy đủ thông tin.");
        }
        else
        {
            lines.Add("Người thực thi do chủ kho chỉ định đã thay gửi giấy chứng tử. "
                + "Bên mình cũng đã kiểm tra và xác nhận đầy đủ thông tin.");
        }
        lines.Add(ContactLine(invitation, executorLabel, hasExecutor));
        if (invitation.VerifierFullName.Trim().Length > 0)
        {
            string card = string.IsNullOrWhiteSpace(invitation.VerifierLawyerCardNumber)
                ? string.Empty
                : $" (thẻ luật sư {invitation.VerifierLawyerCardNumber!.Trim()})";
            lines.Add($"Hồ sơ đã được Người xét duyệt {invitation.VerifierFullName.Trim()}{card} kiểm tra.");
        }
        if (invitation.CaseCode.Trim().Length > 0)
        {
            lines.Add($"Mã hồ sơ: {invitation.CaseCode.Trim()}. Bạn có thể nhập mã này cùng 4 số cuối căn cước vào trang lời mời "
                + "để kiểm tra thư có thật không trước khi làm bất cứ việc gì.");
        }

        string message = (invitation.OwnerMessage ?? string.Empty).Trim();
        if (message.Length > 0)
        {
            lines.Add($"Lời nhắn {ownerLabel} để lại cho bạn:");
            lines.Add("“" + message.Replace("\r", " ").Replace("\n", " ") + "”");
        }

        lines.Add("Bạn chỉ cần làm theo bốn bước sau:");
        lines.Add($"1. Bấm nút “Nhận tài sản” bên dưới. Bạn cần bấm trước {deadline}.");
        lines.Add("2. Đăng nhập bằng đúng email này, chọn Chấp nhận hoặc Từ chối rồi nộp hồ sơ kèm ảnh căn cước hai mặt. "
            + "Người xét duyệt sẽ đối chiếu để biết đúng là bạn.");
        lines.Add($"3. Nhắn {executorLabel} xin khóa mở gói. Họ sẽ gửi bạn một thư riêng chỉ có khóa.");
        lines.Add("4. Nhập khóa, bấm “Đồng ý nhận” rồi tải tài sản về máy.");

        string support = "hãy báo lại cho bên mình";
        if (!string.IsNullOrWhiteSpace(supportContact))
        {
            support = "hãy báo ngay cho bên mình qua " + supportContact!.Trim();
        }
        string note = "Nếu để quá hạn, lời mời hết hiệu lực và bạn sẽ không nhận được phần này. "
            + "Nếu bạn không muốn nhận, cứ đăng nhập rồi bấm “Từ chối”. "
            + "Thư này không có nội dung tài sản. "
            + $"Nếu bạn không quen {ownerLabel} hoặc thấy thư có gì lạ, đừng bấm nút mà {support}. "
            + "Trân trọng, Đội ngũ LegacyVault. " + ScamWarning();

        string leadText = string.Join("\n", lines);

        // Admin chỉ sửa được tiêu đề, tiêu đề lớn, nút và ghi chú; phần thân theo từng hồ sơ nên giữ nguyên.
        EmailParts defaults = new EmailParts(subject, title, leadText, "Nhận tài sản", note);
        if (!applyOverrides)
        {
            return new EmailContent(subject, leadText, string.Empty, EmailTemplateCatalog.HandoverInvitation, defaults);
        }
        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = invitation.FullName;
        tokens["TEN_CHU_KHO"] = ownerLabel;
        tokens["HAN_PHAN_HOI"] = deadline;
        tokens["HAN_NGAN"] = shortDeadline;
        EmailParts parts = EmailTemplateOverrides.Apply(EmailTemplateCatalog.HandoverInvitation, defaults, tokens);

        string text = $"Xin chào {invitation.FullName},\n\n{leadText}\n\n{parts.ButtonText}: {link}\n\n{parts.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            parts.Title,
            invitation.FullName,
            leadText,
            link,
            parts.ButtonText,
            parts.Note,
            "Thông báo bàn giao");
        return new EmailContent(parts.Subject, text, html, EmailTemplateCatalog.HandoverInvitation);
    }

    /// <summary>Cảnh báo lừa đảo kèm thông tin công ty (do Admin đặt) để người nhận tự gọi xác minh.</summary>
    private static string ScamWarning()
    {
        string name = RuntimeSettings.Text("Company.Name", "LegacyVault");
        string phone = RuntimeSettings.Text("Company.Phone", string.Empty);
        List<string> company = new List<string> { name };
        foreach (string key in new[] { "Company.Website", "Company.Email", "Company.Phone", "Company.Address" })
        {
            string value = RuntimeSettings.Text(key, string.Empty);
            if (value.Length > 0)
            {
                company.Add(value);
            }
        }
        string call = phone.Length > 0 ? $" Hãy gọi số {phone} của công ty để xác minh." : string.Empty;
        return "Cảnh báo lừa đảo: LegacyVault không bao giờ hỏi mật khẩu của bạn và không thu tiền qua thư." + call
            + " " + string.Join(" · ", company) + ".";
    }

    private static string ContactLine(HandoverInvitation invitation, string executorLabel, bool hasExecutor)
    {
        if (!hasExecutor)
        {
            return "Nếu có thắc mắc, bạn cứ hỏi Người thực thi do chủ kho chỉ định.";
        }
        bool hasPhone = !string.IsNullOrWhiteSpace(invitation.ExecutorPhone);
        bool hasEmail = !string.IsNullOrWhiteSpace(invitation.ExecutorEmail);
        if (hasPhone && hasEmail)
        {
            return $"Nếu có thắc mắc, bạn cứ gọi {executorLabel} theo số {invitation.ExecutorPhone!.Trim()} "
                + $"hoặc nhắn qua email {invitation.ExecutorEmail.Trim()}.";
        }
        if (hasPhone)
        {
            return $"Nếu có thắc mắc, bạn cứ gọi {executorLabel} theo số {invitation.ExecutorPhone!.Trim()}.";
        }
        if (hasEmail)
        {
            return $"Nếu có thắc mắc, bạn cứ nhắn {executorLabel} qua email {invitation.ExecutorEmail.Trim()}.";
        }
        return $"Nếu có thắc mắc, bạn cứ hỏi {executorLabel}.";
    }
}
