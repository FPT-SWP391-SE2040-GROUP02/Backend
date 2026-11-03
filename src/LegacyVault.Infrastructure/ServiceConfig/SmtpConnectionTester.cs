using System.Diagnostics;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Infrastructure.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Thử cấu hình email mà KHÔNG gửi thư nào: có địa chỉ Webhook thì gọi GET tới địa chỉ đó (đạt khi dịch vụ trả lời mà không lỗi
/// phía máy chủ), không thì kết nối SMTP, đăng nhập nếu có tài khoản rồi ngắt. Kết quả chỉ có Ok, mã lỗi ngắn và độ trễ.
/// </summary>
public class SmtpConnectionTester : IServiceConnectionTester
{
    private const int MaxTimeoutSeconds = 10;

    private readonly IHttpClientFactory httpClientFactory;

    public SmtpConnectionTester(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public string ServiceKey
    {
        get { return SystemConfigServices.Smtp; }
    }

    public async Task<TestSystemConfigResponse> TestAsync(ResolvedConfig draft, CancellationToken ct)
    {
        TestSystemConfigResponse response = new TestSystemConfigResponse();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            SmtpSettings settings = SmtpSettingsMapping.FromConfig(draft);
            int timeoutSeconds = MaxTimeoutSeconds;
            if (settings.TimeoutSeconds > 0 && settings.TimeoutSeconds < MaxTimeoutSeconds)
            {
                timeoutSeconds = settings.TimeoutSeconds;
            }
            string webhookUrl = settings.ResolveWebhookUrl();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                if (webhookUrl.Length > 0)
                {
                    response.ErrorCode = await ProbeWebhookAsync(webhookUrl, timeout.Token);
                }
                else
                {
                    await ProbeSmtpAsync(settings, timeoutSeconds, timeout.Token);
                }
            }
            response.Ok = response.ErrorCode == null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            response.ErrorCode = SmtpMailSender.ErrorCodeOf(ex);
        }
        response.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
        return response;
    }

    /// <summary>Trả null khi Webhook trả lời được, ngược lại mã lỗi HTTP.</summary>
    private async Task<string?> ProbeWebhookAsync(string webhookUrl, CancellationToken ct)
    {
        HttpClient client = httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        using (HttpResponseMessage http = await client.GetAsync(webhookUrl, ct))
        {
            int code = (int)http.StatusCode;
            if (ServiceFailover.ShouldFailOver(code))
            {
                return "HTTP_" + code;
            }
            return null;
        }
    }

    /// <summary>Kết nối, đăng nhập (nếu có tài khoản) rồi ngắt; không gửi thư. Lỗi nào cũng ném ra để quy thành mã lỗi.</summary>
    protected virtual async Task ProbeSmtpAsync(SmtpSettings settings, int timeoutSeconds, CancellationToken ct)
    {
        SecureSocketOptions? secureSocket = settings.ParseSecureSocket();
        if (secureSocket == null)
        {
            throw new InvalidOperationException("SecureSocket không hợp lệ.");
        }
        using (SmtpClient client = new SmtpClient())
        {
            client.Timeout = timeoutSeconds * 1000;
            client.CheckCertificateRevocation = false;
            await client.ConnectAsync(settings.ResolveHost(), settings.Port, secureSocket.Value, ct);
            string userName = settings.ResolveUserName();
            if (userName.Length > 0)
            {
                await client.AuthenticateAsync(userName, settings.ResolvePassword(), ct);
            }
            await client.DisconnectAsync(true, ct);
        }
    }
}
