using System.Net;
using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Gửi thư mời Executor qua SMTP khi có cấu hình; trên môi trường Development hỗ trợ ghi log liên kết
/// và đánh dấu đã gửi để developer không bị chặn luồng kiểm thử khi chưa có SMTP thật.
/// </summary>
public sealed class ExecutorInvitationSender(
    SmtpMailSender sender,
    IOptions<SmtpSettings> settings,
    IConfiguration config,
    IAppUrlProvider urlProvider,
    IHostEnvironment env,
    ILogger<ExecutorInvitationSender> logger) : IExecutorInvitationSender
{
    public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        return DeliverAsync(email, null, token, expiresAt, false, ct);
    }

    public Task<bool> SendAssignedByAdminAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        return DeliverAsync(email, null, token, expiresAt, true, ct);
    }

    public Task<bool> SendFromOwnerAsync(string email, string ownerName, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        return DeliverAsync(email, ownerName, token, expiresAt, false, ct);
    }

    private const string AboutText =
        "LegacyVault là nơi lưu trữ tài sản số được mã hóa, chỉ bàn giao cho người thân sau khi hồ sơ được xét duyệt.";

    // Khớp trang Liên hệ của web (publicInfo.content.ts). Đổi ở một nơi thì đổi nơi kia.
    private const string ContactEmail = "legacyvault8386@gmail.com";
    private const string ContactHours = "8h–22h, nghỉ thứ Bảy và Chủ nhật";

    /// <summary>Trang giới thiệu (trang chủ) cùng địa chỉ web với liên kết lời mời.</summary>
    private static string LandingUrlOf(string invitationLink)
    {
        Uri uri = new Uri(invitationLink);
        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }

    /// <summary>Nội dung một thư mời Executor: tiêu đề thư, bản văn bản thuần và bản HTML.</summary>
    public sealed record InvitationMail(string Subject, string Text, string Html);

    /// <summary>
    /// Dựng nội dung thư mời. ownerName có giá trị thì thư nêu rõ tên Chủ kho đã mời; assignedByAdmin thì thư nói do
    /// quản trị viên chỉ định. Tách riêng để bản xem thử và bản gửi thật dùng chung một nội dung.
    /// </summary>
    public static InvitationMail BuildMail(
        string email, string? ownerName, string link, DateTimeOffset expiresAt, bool assignedByAdmin)
    {
        bool hasOwner = !string.IsNullOrWhiteSpace(ownerName);

        string subject;
        string title;
        string lead;
        string who;
        if (assignedByAdmin)
        {
            who = "Quản trị viên LegacyVault";
            subject = "Quản trị viên LegacyVault mời bạn làm Người thi hành di chúc";
            title = "Quản trị viên LegacyVault mời bạn làm Người thi hành di chúc";
            lead = "Quản trị viên LegacyVault chọn anh/chị là người đứng ra lo việc bàn giao tài sản số của một kho đang cần người phụ trách, "
                + "cho người thân của Chủ kho khi Chủ kho qua đời. Mỗi kho chỉ có một người được giao nhiệm vụ như vậy.";
        }
        else if (hasOwner)
        {
            string name = ownerName!.Trim();
            who = "Ông/Bà " + name;
            subject = "Ông/Bà " + name + " mời bạn làm Người thi hành di chúc";
            title = name + " mời bạn làm Người thi hành di chúc";
            lead = "Ông/Bà " + name + " đang lưu giữ tài sản số của mình trên LegacyVault và đã chọn anh/chị là người đứng ra lo việc "
                + "bàn giao số tài sản này cho người thân khi ông/bà qua đời. Mỗi kho chỉ có một người được giao nhiệm vụ như vậy.";
        }
        else
        {
            who = "Một Chủ kho";
            subject = "Lời mời làm Người thi hành di chúc — LegacyVault";
            title = "Bạn được mời làm Người thi hành di chúc";
            lead = "Một Chủ kho trên LegacyVault đã chọn anh/chị là người đứng ra lo việc bàn giao tài sản số của họ cho người thân "
                + "khi họ qua đời. Mỗi kho chỉ có một người được giao nhiệm vụ như vậy.";
        }

        string text = who + " mời anh/chị làm Người thi hành di chúc trên LegacyVault. Xem và trả lời tại " + link
            + " . Lời mời hết hạn " + expiresAt.ToString("O") + ". Không chuyển tiếp liên kết này.";

        List<LegacyVaultEmailTheme.MailListSection> sections = new List<LegacyVaultEmailTheme.MailListSection>();
        sections.Add(new LegacyVaultEmailTheme.MailListSection("Nếu nhận lời, anh/chị sẽ:", new string[]
        {
            "Điểm danh thay khi Chủ kho trễ hạn điểm danh.",
            "Báo tin và nộp giấy chứng tử khi Chủ kho qua đời.",
            "Giữ mảnh khóa Chủ kho giao, và gửi khóa mở gói cho người nhận khi đến lúc.",
            "Theo dõi việc bàn giao cho đến khi xong.",
        }));

        List<LegacyVaultEmailTheme.MailParagraph> closing = new List<LegacyVaultEmailTheme.MailParagraph>();
        closing.Add(new LegacyVaultEmailTheme.MailParagraph(
            "Phạm vi nhiệm vụ có giới hạn:",
            "anh/chị chỉ thấy danh sách tài sản, không đọc được nội dung bên trong. "
            + "Hồ sơ báo tin do Người xét duyệt của LegacyVault quyết định, không phải anh/chị."));
        closing.Add(new LegacyVaultEmailTheme.MailParagraph(
            null,
            "Anh/chị có thể nhận lời hoặc từ chối. Chủ kho sẽ được báo lại lựa chọn của anh/chị."));

        string notice = "Lời mời có hiệu lực đến "
            + expiresAt.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm 'ngày' dd/MM/yyyy")
            + ". Không chuyển tiếp liên kết này cho người khác.";
        if (hasOwner && !assignedByAdmin)
        {
            notice += " Nếu anh/chị không quen người này, hãy bỏ qua thư.";
        }

        string html = LegacyVaultEmailTheme.BuildHtml(
            title,
            email,
            lead,
            link,
            "Xem và trả lời thư mời",
            notice,
            "Thư mời",
            false,
            AboutText,
            LandingUrlOf(link),
            "Tìm hiểu thêm",
            ContactEmail,
            null,
            ContactHours,
            sections,
            "Kính gửi anh/chị,",
            closing);
        return new InvitationMail(subject, text, html);
    }

    private async Task<bool> DeliverAsync(
        string email, string? ownerName, string token, DateTimeOffset expiresAt, bool assignedByAdmin, CancellationToken ct)
    {
        var link = urlProvider.GetExecutorInvitationUrl(token);

        bool deliveryEnabled = config.GetValue<bool?>("ExecutorInvitations:DeliveryEnabled")
            ?? (config.GetValue<bool?>("ExecutorInvitations__DeliveryEnabled") ?? settings.Value.IsConfigured);

        if (deliveryEnabled && settings.Value.IsConfigured)
        {
            InvitationMail mail = BuildMail(email, ownerName, link, expiresAt, assignedByAdmin);

            try
            {
                await sender.SendAsync(email, "", mail.Subject, mail.Text, mail.Html, ct);
                logger.LogInformation("Executor invitation successfully sent to {Email} via SMTP.", email);
                return true;
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Executor invitation delivery to {Email} failed ({ExceptionType}): {Message}.", email, exception.GetType().Name, exception.Message);
            }
        }

        // Trên môi trường Development: log link lời mời để test và ghi nhận trạng thái đã gửi
        if (env.IsDevelopment())
        {
            logger.LogInformation(
                "\n======================================================================\n" +
                "[DEV - LOI MOI EXECUTOR] Gui toi: {Email}\n" +
                "Link nhan nhiem vu: {Link}\n" +
                "Loi moi het han: {ExpiresAt:O}\n" +
                "======================================================================\n",
                email, link, expiresAt);
            return true;
        }

        return false;
    }
}
