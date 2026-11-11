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

    /// <summary>
    /// Mẫu thân thư gốc. Các biến {TEN_...} được thay khi gửi; biến câu (người gửi giấy, liên hệ, Người xét duyệt, mã hồ sơ, lời nhắn)
    /// tự mất cùng dòng của nó khi hồ sơ không có dữ liệu. Admin sửa mẫu này ở trang Mẫu thư.
    /// </summary>
    public const string DefaultBodyTemplate =
        "Trước đây {TEN_CHU_KHO} đã báo cho bạn biết bạn được chọn là người nhận một phần tài sản số của họ trên LegacyVault. "
        + "Nay đã đến lúc bạn nhận phần đó: người này đã qua đời nên không thể tự gửi lời nhắn này.\n"
        + "{NGUOI_GUI_GIAY_CHUNG_TU}\n"
        + "{LIEN_HE_NGUOI_THUC_THI}\n"
        + "{NGUOI_XET_DUYET}\n"
        + "{MA_HO_SO}\n"
        + "{LOI_NHAN_CHU_KHO}\n"
        + "Bạn chỉ cần làm theo bốn bước sau:\n"
        + "1. Bấm nút “Nhận tài sản” bên dưới. Bạn cần bấm trước {HAN_PHAN_HOI}.\n"
        + "2. Đăng nhập bằng đúng email này, chọn Chấp nhận hoặc Từ chối rồi nộp hồ sơ kèm ảnh căn cước hai mặt. "
        + "Người xét duyệt sẽ đối chiếu để biết đúng là bạn.\n"
        + "3. Nhắn {TEN_NGUOI_THUC_THI} xin khóa mở gói. Họ sẽ gửi bạn một thư riêng chỉ có khóa.\n"
        + "4. Nhập khóa, bấm “Đồng ý nhận” rồi tải tài sản về máy.";

    /// <summary>Chữ gốc của tiêu đề, thân, nút và ghi chú với biến giữ nguyên, để Admin sao chép làm chỗ bắt đầu.</summary>
    public static EmailParts SampleParts()
    {
        return new EmailParts(
            "[Cần xử lý trước {HAN_NGAN}] Bạn có thể nhận tài sản số {TEN_CHU_KHO} để lại",
            "Đã đến lúc bạn nhận phần tài sản {TEN_CHU_KHO} để lại",
            DefaultBodyTemplate,
            "Nhận tài sản",
            NoteWithoutWarning("{TEN_CHU_KHO}", null));
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

        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = invitation.FullName;
        tokens["TEN_CHU_KHO"] = ownerLabel;
        tokens["TEN_NGUOI_THUC_THI"] = executorLabel;
        tokens["HAN_PHAN_HOI"] = deadline;
        tokens["HAN_NGAN"] = shortDeadline;
        tokens["NGUOI_GUI_GIAY_CHUNG_TU"] = SenderLine(executor, ownerLabel, hasExecutor);
        tokens["LIEN_HE_NGUOI_THUC_THI"] = ContactLine(invitation, executorLabel, hasExecutor);
        tokens["NGUOI_XET_DUYET"] = VerifierLine(invitation);
        tokens["MA_HO_SO"] = CaseCodeLine(invitation);
        tokens["LOI_NHAN_CHU_KHO"] = OwnerMessageBlock(invitation, ownerLabel);

        EmailParts defaults = new EmailParts(
            EmailTemplateOverrides.Fill("[Cần xử lý trước {HAN_NGAN}] Bạn có thể nhận tài sản số {TEN_CHU_KHO} để lại", tokens),
            EmailTemplateOverrides.Fill("Đã đến lúc bạn nhận phần tài sản {TEN_CHU_KHO} để lại", tokens),
            DefaultBodyTemplate,
            "Nhận tài sản",
            NoteWithoutWarning(ownerLabel, supportContact));
        if (!applyOverrides)
        {
            return new EmailContent(defaults.Subject, defaults.Body, string.Empty, EmailTemplateCatalog.HandoverInvitation, defaults);
        }

        // Thân thư là mẫu có biến (của Admin hoặc mẫu gốc); các biến câu rỗng thì dòng của chúng bị bỏ.
        EmailParts parts = EmailTemplateOverrides.Apply(EmailTemplateCatalog.HandoverInvitation, defaults, tokens);
        string body = EmailTemplateOverrides.Fill(parts.Body, tokens);
        body = DropEmptyLines(body);
        // Cảnh báo lừa đảo kèm thông tin công ty luôn nằm cuối ghi chú, Admin không sửa hay xóa được.
        string note = parts.Note + " " + ScamWarning();

        string text = $"Xin chào {invitation.FullName},\n\n{body}\n\n{parts.ButtonText}: {link}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            parts.Title,
            invitation.FullName,
            body,
            link,
            parts.ButtonText,
            note,
            "Thông báo bàn giao");
        return new EmailContent(parts.Subject, text, html, EmailTemplateCatalog.HandoverInvitation);
    }

    private static string DropEmptyLines(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines);
    }

    private static string NoteWithoutWarning(string ownerLabel, string? supportContact)
    {
        string support = "hãy báo lại cho bên mình";
        if (!string.IsNullOrWhiteSpace(supportContact))
        {
            support = "hãy báo ngay cho bên mình qua " + supportContact!.Trim();
        }
        return "Nếu để quá hạn, lời mời hết hiệu lực và bạn sẽ không nhận được phần này. "
            + "Nếu bạn không muốn nhận, cứ đăng nhập rồi bấm “Từ chối”. "
            + "Thư này không có nội dung tài sản. "
            + $"Nếu bạn không quen {ownerLabel} hoặc thấy thư có gì lạ, đừng bấm nút mà {support}. "
            + "Trân trọng, Đội ngũ LegacyVault.";
    }

    private static string SenderLine(string executor, string ownerLabel, bool hasExecutor)
    {
        if (hasExecutor)
        {
            return $"Người được tin tưởng nhất là {executor}, người thực thi di chúc của {ownerLabel}, đã thay gửi giấy chứng tử. "
                + "Bên mình cũng đã kiểm tra và xác nhận đầy đủ thông tin.";
        }
        return "Người thực thi do chủ kho chỉ định đã thay gửi giấy chứng tử. "
            + "Bên mình cũng đã kiểm tra và xác nhận đầy đủ thông tin.";
    }

    private static string VerifierLine(HandoverInvitation invitation)
    {
        if (invitation.VerifierFullName.Trim().Length == 0)
        {
            return string.Empty;
        }
        string card = string.IsNullOrWhiteSpace(invitation.VerifierLawyerCardNumber)
            ? string.Empty
            : $" (thẻ luật sư {invitation.VerifierLawyerCardNumber!.Trim()})";
        return $"Hồ sơ đã được Người xét duyệt {invitation.VerifierFullName.Trim()}{card} kiểm tra.";
    }

    private static string CaseCodeLine(HandoverInvitation invitation)
    {
        if (invitation.CaseCode.Trim().Length == 0)
        {
            return string.Empty;
        }
        return $"Mã hồ sơ: {invitation.CaseCode.Trim()}.";
    }

    private static string OwnerMessageBlock(HandoverInvitation invitation, string ownerLabel)
    {
        string message = (invitation.OwnerMessage ?? string.Empty).Trim();
        if (message.Length == 0)
        {
            return string.Empty;
        }
        return $"Lời nhắn {ownerLabel} để lại cho bạn:\n“" + message.Replace("\r", " ").Replace("\n", " ") + "”";
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
