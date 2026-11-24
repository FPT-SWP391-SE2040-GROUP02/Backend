using System.Globalization;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng email mời nhận tài sản. Hàm thuần, không gửi gì.
/// </summary>
/// <remarks>
/// - Không có tên gói hay tên tài sản; chỉ có tên người nhận, tên Chủ kho, liên hệ Người thực thi, link và hạn phản hồi.
/// - Đây là thư thứ hai người nhận được: lần đầu họ đã được báo khi Chủ kho bật kế hoạch bàn giao, nên thư nhắc lại chuyện cũ.
/// - Không đoán giới tính từ tên: dùng "bạn" cho người nhận và chỉ dùng họ tên cho Chủ kho, Người thực thi.
/// - Giờ hiển thị theo giờ Việt Nam (UTC+7). Mọi chữ do người dùng nhập được mã hóa HTML trong bản HTML.
/// </remarks>
public static class HandoverInvitationEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Bản rút gọn khi chưa có thông tin liên hệ của kho (dữ liệu cũ, test).</summary>
    public static EmailContent Build(string fullName, string link, DateTimeOffset responseDeadline)
    {
        HandoverInvitation invitation = new HandoverInvitation
        {
            FullName = fullName,
            ResponseDeadline = responseDeadline
        };
        return Build(invitation, link, null);
    }

    /// <summary>Hạn phản hồi theo giờ Việt Nam, dạng giờ và ngày đầy đủ.</summary>
    private static string LongDeadline(HandoverInvitation invitation)
    {
        return invitation.ResponseDeadline.ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Hạn phản hồi theo giờ Việt Nam, dạng ngày/tháng ngắn.</summary>
    private static string ShortDeadline(HandoverInvitation invitation)
    {
        return invitation.ResponseDeadline.ToOffset(VietnamOffset)
            .ToString("dd/MM", CultureInfo.InvariantCulture);
    }

    /// <summary>Cách gọi Chủ kho trong thư: họ tên, hoặc chữ "chủ kho" khi chưa có tên.</summary>
    private static string OwnerLabel(HandoverInvitation invitation)
    {
        string owner = invitation.OwnerFullName.Trim();
        if (owner.Length == 0)
        {
            return "chủ kho";
        }
        return owner;
    }

    /// <summary>
    /// Chữ gốc của thư mời: tiêu đề thư, tiêu đề lớn, thân thư theo từng hồ sơ, nút và ghi chú.
    /// Admin chỉ sửa được tiêu đề, tiêu đề lớn, nút và ghi chú; phần thân giữ nguyên.
    /// </summary>
    private static EmailParts DefaultParts(HandoverInvitation invitation, string? supportContact)
    {
        string deadline = LongDeadline(invitation);
        string shortDeadline = ShortDeadline(invitation);
        string ownerLabel = OwnerLabel(invitation);
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
            lines.Add($"{executor}, Người thực thi của {ownerLabel}, đã mở bàn giao. "
                + "Luật sư đã lập biên bản bàn giao, trong đó có tên bạn.");
        }
        else
        {
            lines.Add("Người thực thi do chủ kho chỉ định đã mở bàn giao. "
                + "Luật sư đã lập biên bản bàn giao, trong đó có tên bạn.");
        }
        lines.Add(ContactLine(invitation, executorLabel, hasExecutor));
        if (invitation.VerifierFullName.Trim().Length > 0)
        {
            string card = string.Empty;
            if (!string.IsNullOrWhiteSpace(invitation.VerifierLawyerCardNumber))
            {
                card = $" (thẻ luật sư {invitation.VerifierLawyerCardNumber.Trim()})";
            }
            lines.Add($"Biên bản do luật sư {invitation.VerifierFullName.Trim()}{card} lập và kiểm.");
        }
        if (invitation.CaseCode.Trim().Length > 0)
        {
            lines.Add($"Mã hồ sơ: {invitation.CaseCode.Trim()}.");
        }

        string message = string.Empty;
        if (invitation.OwnerMessage != null)
        {
            message = invitation.OwnerMessage.Trim();
        }
        if (message.Length > 0)
        {
            lines.Add($"Lời nhắn {ownerLabel} để lại cho bạn:");
            lines.Add("“" + message.Replace("\r", " ").Replace("\n", " ") + "”");
        }

        lines.Add("Bạn chỉ cần làm theo bốn bước sau:");
        lines.Add($"1. Bấm nút “Nhận tài sản” bên dưới. Bạn cần bấm trước {deadline}.");
        lines.Add("2. Đăng nhập bằng đúng email này, chọn Chấp nhận hoặc Từ chối rồi nộp hồ sơ kèm ảnh căn cước hai mặt. "
            + "Người xét duyệt sẽ đối chiếu để biết đúng là bạn.");
        lines.Add($"3. Nhắn {executorLabel} xin mã nhận. Họ sẽ gửi bạn một thư riêng chỉ có mã nhận.");
        lines.Add("4. Nhập mã nhận, bấm “Đồng ý nhận” rồi tải tài sản về máy.");

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
        return new EmailParts(subject, title, leadText, "Nhận tài sản", note);
    }

    /// <summary>Dựng thư mời hoàn chỉnh: ghép bản chữ và bản HTML.</summary>
    /// <param name="supportContact">Câu liên hệ hỗ trợ của nhóm, ví dụ "email ho-tro@... hoặc số 090...". Null thì chỉ bảo báo lại cho LegacyVault.</param>
    public static EmailContent Build(HandoverInvitation invitation, string link, string? supportContact)
    {
        EmailParts parts = DefaultParts(invitation, supportContact);
        string leadText = parts.Body;

        string text = $"Xin chào {invitation.FullName},\n\n{leadText}\n\n{parts.ButtonText}: {link}\n\n{parts.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            parts.Title,
            invitation.FullName,
            leadText,
            link,
            parts.ButtonText,
            parts.Note,
            "Thông báo bàn giao");
        return new EmailContent(parts.Subject, text, html);
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
        string call = string.Empty;
        if (phone.Length > 0)
        {
            call = $" Hãy gọi số {phone} của công ty để xác minh.";
        }
        return "Cảnh báo lừa đảo: LegacyVault không bao giờ hỏi mật khẩu của bạn và không thu tiền qua thư." + call
            + " " + string.Join(" · ", company) + ".";
    }

    /// <summary>Câu liên hệ trong thư: có Người thực thi thì kèm số điện thoại và/hoặc email của họ, không thì bảo hỏi Người thực thi do chủ kho chỉ định.</summary>
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
