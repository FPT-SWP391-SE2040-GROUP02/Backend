using System.Net;
using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Explicitly enabled SMTP only. Missing/failed delivery is not reported as sent; token is never logged.</summary>
public sealed class ExecutorInvitationSender(SmtpMailSender sender, IOptions<SmtpSettings> settings,
    IConfiguration config, ILogger<ExecutorInvitationSender> logger) : IExecutorInvitationSender
{
    public async Task<bool> SendAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!config.GetValue<bool>("ExecutorInvitations:DeliveryEnabled") || !settings.Value.IsConfigured) return false;
        var baseUrl = config["App:ClientUrl"]?.TrimEnd('/') ?? "http://localhost:5173";
        var link = baseUrl + "/executor-invitation#token=" + Uri.EscapeDataString(token);
        var text = $"Bạn được mời làm Executor {(role == "PRIMARY" ? "chính" : "dự phòng")} trên LegacyVault. Đọc phạm vi nhiệm vụ và tự quyết định tại {link}. Lời mời hết hạn {expiresAt:O}. Không chuyển tiếp liên kết này. Bỏ qua thư nếu bạn không chấp nhận nhiệm vụ.";
        string roleName = role == "PRIMARY" ? "Người thi hành chính (Primary Executor)" : "Người thi hành dự phòng (Backup Executor)";
        string lead = $"Bạn vừa được một Chủ kho trên LegacyVault tin tưởng chỉ định đảm nhiệm vai trò {roleName}. "
            + "Trong vai trò này, bạn sẽ hỗ trợ kích hoạt và thực thi kế hoạch bàn giao di sản số khi có sự kiện diễn ra. Vui lòng nhấn vào nút bên dưới để xem phạm vi nhiệm vụ và quyết định chấp nhận hoặc từ chối.";
        string notice = $"Lời mời này có hiệu lực đến {expiresAt.ToOffset(TimeSpan.FromHours(7)):HH:mm 'ngày' dd/MM/yyyy}. Vì lý do bảo mật, không chuyển tiếp liên kết này cho người khác. Nếu bạn không muốn đảm nhiệm vai trò này, bạn có thể từ chối hoặc bỏ qua email.";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Lời mời đảm nhiệm vai trò Người thi hành di chúc (Executor)",
            email,
            lead,
            link,
            "Xem & Phản hồi lời mời Executor",
            notice);

        try
        {
            await sender.SendAsync(email, "", "Lời mời nhiệm vụ Executor — LegacyVault", text, html, ct);
            return true;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Executor invitation delivery unconfirmed ({ExceptionType}).", exception.GetType().Name);
            return false;
        }
    }
}
