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
        try
        {
            await sender.SendAsync(email, "", "Lời mời nhiệm vụ Executor — LegacyVault", text,
                "<p>" + WebUtility.HtmlEncode(text) + "</p>", ct);
            return true;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Executor invitation delivery unconfirmed ({ExceptionType}).", exception.GetType().Name);
            return false;
        }
    }
}
