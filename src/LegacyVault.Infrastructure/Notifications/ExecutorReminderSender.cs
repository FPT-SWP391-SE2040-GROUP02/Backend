using System.Net;
using LegacyVault.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Thư nhắc lời mời Executor ở giờ thứ 48 (SETUP-20). Cùng công tắc với thư mời: chỉ gửi khi ExecutorInvitations:DeliveryEnabled
/// và Email:Smtp đã cấu hình; chưa bật thì trả false (không giả vờ đã gửi). Không có link vì DB không giữ token gốc.
/// </summary>
public sealed class ExecutorReminderSender(SmtpMailSender sender, IOptions<SmtpSettings> settings,
    IConfiguration config, ILogger<ExecutorReminderSender> logger) : IExecutorReminderSender
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<bool> SendAsync(string email, string role, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!config.GetValue<bool>("ExecutorInvitations:DeliveryEnabled") || !settings.Value.IsConfigured)
        {
            return false;
        }
        string deadline = expiresAt.ToOffset(VietnamOffset).ToString("HH:mm 'ngày' dd/MM/yyyy");
        string roleText = "dự phòng";
        if (role == "PRIMARY")
        {
            roleText = "chính";
        }
        string text = $"Bạn có lời mời làm Executor {roleText} trên LegacyVault chưa trả lời, hết hạn lúc {deadline}. "
            + "Hãy mở liên kết trong thư mời trước đó để chấp nhận hoặc từ chối; nếu không còn thư đó, nhờ người mời gửi lại lời mời. "
            + "Bỏ qua thư này nếu bạn không biết người mời.";
        try
        {
            await sender.SendAsync(email, "", "Nhắc: lời mời Executor trên LegacyVault sắp hết hạn", text,
                "<p>" + WebUtility.HtmlEncode(text) + "</p>", ct);
            return true;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Executor reminder delivery unconfirmed ({ExceptionType}).", exception.GetType().Name);
            return false;
        }
    }
}
