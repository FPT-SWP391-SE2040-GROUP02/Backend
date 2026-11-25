using System.Net;
using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Cùng công tắc với thư mời Người thực thi; chưa bật thì trả false, không giả vờ đã gửi.</summary>
public sealed class ExecutorWillNoticeSender : IExecutorWillNoticeSender
{
    private readonly SmtpMailSender sender;
    private readonly IOptions<SmtpSettings> settings;
    private readonly IConfiguration config;
    private readonly ILogger<ExecutorWillNoticeSender> logger;

    public ExecutorWillNoticeSender(SmtpMailSender sender, IOptions<SmtpSettings> settings,
        IConfiguration config, ILogger<ExecutorWillNoticeSender> logger)
    {
        this.sender = sender;
        this.settings = settings;
        this.config = config;
        this.logger = logger;
    }

    public Task<bool> SendNewVersionAsync(string executorEmail, string ownerName, int versionNo, CancellationToken ct)
    {
        string text = $"Chủ kho {ownerName} vừa ký biên bản phân chia phiên bản {versionNo}. "
            + "Hãy đăng nhập LegacyVault, mở biên bản để xem rồi chọn đồng ý hoặc không đồng ý.";
        return SendAsync(executorEmail, "Biên bản phân chia có phiên bản mới cần bạn xem", text, ct);
    }

    public Task<bool> SendDeclinedAsync(string ownerEmail, string executorName, int versionNo, CancellationToken ct)
    {
        string text = $"Người thực thi {executorName} không đồng ý biên bản phân chia phiên bản {versionNo}. "
            + "Bạn có thể sửa và ký lại, hoặc mời Người thực thi khác trong LegacyVault.";
        return SendAsync(ownerEmail, "Người thực thi không đồng ý biên bản phân chia", text, ct);
    }

    private async Task<bool> SendAsync(string email, string subject, string text, CancellationToken ct)
    {
        bool deliveryEnabled = config.GetValue<bool?>("ExecutorInvitations:DeliveryEnabled") ?? settings.Value.IsConfigured;
        if (!deliveryEnabled || !settings.Value.IsConfigured)
        {
            return false;
        }
        try
        {
            await sender.SendAsync(email, "", subject, text, "<p>" + WebUtility.HtmlEncode(text) + "</p>", ct);
            return true;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Executor will notice delivery failed ({ExceptionType}).", exception.GetType().Name);
            return false;
        }
    }
}
