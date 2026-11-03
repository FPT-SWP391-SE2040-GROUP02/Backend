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
    public async Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var link = urlProvider.GetExecutorInvitationUrl(token);

        bool deliveryEnabled = config.GetValue<bool?>("ExecutorInvitations:DeliveryEnabled")
            ?? (config.GetValue<bool?>("ExecutorInvitations__DeliveryEnabled") ?? settings.Value.IsConfigured);

        if (deliveryEnabled && settings.Value.IsConfigured)
        {
            var text = $"Bạn được mời làm Executor trên LegacyVault. Đọc phạm vi nhiệm vụ và tự quyết định tại {link} . Lời mời hết hạn {expiresAt:O}. Không chuyển tiếp liên kết này. Bỏ qua thư nếu bạn không chấp nhận nhiệm vụ.";
            string lead = $"Bạn vừa được một Chủ kho trên LegacyVault tin tưởng chỉ định đảm nhiệm vai trò Người thi hành (Executor). "
                + "Trong vai trò này, bạn sẽ hỗ trợ kích hoạt và thực thi kế hoạch bàn giao di sản số khi có sự kiện diễn ra. Vui lòng nhấn vào nút bên dưới để xem phạm vi nhiệm vụ và quyết định chấp nhận hoặc từ chối.";
            string notice = $"Lời mời này có hiệu lực đến {expiresAt.ToOffset(TimeSpan.FromHours(7)):HH:mm 'ngày' dd/MM/yyyy}. Vì lý do bảo mật, không chuyển tiếp liên kết này cho người khác. Nếu bạn không muốn đảm nhiệm vai trò này, bạn có thể từ chối hoặc bỏ qua email.";
            string html = LegacyVaultEmailTheme.BuildHtml(
                "Lời mời đảm nhiệm vai trò Người thi hành di chúc (Executor)",
                email,
                lead,
                link,
                "Xem & Phản hồi lời mời Executor",
                notice,
                "Lời mời nhiệm vụ");

            try
            {
                await sender.SendAsync(email, "", "Lời mời nhiệm vụ Executor — LegacyVault", text, html, ct);
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
