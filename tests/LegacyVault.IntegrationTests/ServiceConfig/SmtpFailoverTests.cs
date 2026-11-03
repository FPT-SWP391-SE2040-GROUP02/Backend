using System.Net.Sockets;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.Infrastructure.ServiceConfig;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>
/// SmtpMailSender đọc máy chủ, tài khoản, mật khẩu từ cấu hình lúc chạy và thử ô dự phòng khi ô chính gửi thư lỗi.
/// Đường gửi thật qua máy chủ SMTP giả đã có ở SmtpEmailTests; ở đây giả lập bước gửi để kiểm luật chuyển ô. Không cần DB.
/// </summary>
public sealed class SmtpFailoverTests
{
    private sealed class RecordingSender : SmtpMailSender
    {
        public RecordingSender(IServiceScopeFactory scopes, IServiceStatusTracker tracker)
            : base(scopes, tracker, TimeProvider.System)
        {
        }

        public List<SmtpSettings> Used { get; } = new List<SmtpSettings>();

        public Func<SmtpSettings, Exception?> Fail { get; set; } = settings => null;

        protected override Task DeliverAsync(
            SmtpSettings settings, string toEmail, string toName, string subject, string textBody, string htmlBody,
            CancellationToken ct)
        {
            Used.Add(settings);
            Exception? failure = Fail(settings);
            if (failure != null)
            {
                throw failure;
            }
            return Task.CompletedTask;
        }
    }

    private static SmtpSettings Settings(string host, string password, int port = 587, string webhook = "")
    {
        SmtpSettings settings = new SmtpSettings();
        settings.Host = host;
        settings.Port = port;
        settings.SecureSocket = "StartTls";
        settings.UserName = "no-reply@" + host;
        settings.Password = password;
        settings.FromAddress = "no-reply@" + host;
        settings.FromName = "LegacyVault";
        settings.TimeoutSeconds = 20;
        settings.WebhookUrl = webhook;
        return settings;
    }

    private static (RecordingSender Sender, ServiceStatusTracker Tracker) Create(bool withFallback)
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        resolver.Slots[SystemConfigSlot.Primary] = SmtpTestSenders.ConfigFrom(Settings("primary.mail.test", "primary-pass-1111", 465));
        if (withFallback)
        {
            resolver.Slots[SystemConfigSlot.Fallback] = SmtpTestSenders.ConfigFrom(Settings("fallback.mail.test", "fallback-pass-2222"));
        }
        ServiceStatusTracker tracker = new ServiceStatusTracker();
        return (new RecordingSender(SmtpTestSenders.ScopesFor(resolver), tracker), tracker);
    }

    private static Task SendAsync(SmtpMailSender sender, CancellationToken ct = default)
    {
        return sender.SendAsync("an@test.local", "An", "Tiêu đề", "chữ", "<p>html</p>", ct);
    }

    [Fact]
    public async Task UsesPrimarySettingsFromConfig()
    {
        (RecordingSender sender, ServiceStatusTracker tracker) = Create(true);

        await SendAsync(sender);

        SmtpSettings used = Assert.Single(sender.Used);
        Assert.Equal("primary.mail.test", used.Host);
        Assert.Equal(465, used.Port);
        Assert.Equal("StartTls", used.SecureSocket);
        Assert.Equal("no-reply@primary.mail.test", used.UserName);
        Assert.Equal("primary-pass-1111", used.Password);
        Assert.Equal(20, used.TimeoutSeconds);
        Assert.Equal(SystemConfigSlot.Primary, tracker.Get(SystemConfigServices.Smtp).ActiveSlot);
    }

    [Fact]
    public async Task PrimaryFails_FallbackSendsTheMail_AndTrackerReportsFallback()
    {
        (RecordingSender sender, ServiceStatusTracker tracker) = Create(true);
        sender.Fail = settings => settings.Host == "primary.mail.test" ? new InvalidOperationException("down") : null;

        await SendAsync(sender);

        Assert.Equal(2, sender.Used.Count);
        Assert.Equal("fallback.mail.test", sender.Used[1].Host);
        Assert.Equal("fallback-pass-2222", sender.Used[1].Password);
        ServiceStatus status = tracker.Get(SystemConfigServices.Smtp);
        Assert.Equal(SystemConfigSlot.Fallback, status.ActiveSlot);
        Assert.True(status.LastSucceeded);
    }

    [Fact]
    public async Task NoFallback_PrimaryFails_ThrowsTheOriginalError()
    {
        (RecordingSender sender, ServiceStatusTracker tracker) = Create(false);
        InvalidOperationException failure = new InvalidOperationException("smtp down");
        sender.Fail = settings => failure;

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(sender));

        Assert.Same(failure, thrown);
        Assert.Single(sender.Used);
        Assert.False(tracker.Get(SystemConfigServices.Smtp).LastSucceeded);
    }

    [Fact]
    public async Task BothFail_ThrowsThePrimaryError_AfterExactlyTwoAttempts()
    {
        (RecordingSender sender, ServiceStatusTracker tracker) = Create(true);
        InvalidOperationException primaryFailure = new InvalidOperationException("primary down");
        sender.Fail = settings => settings.Host == "primary.mail.test" ? primaryFailure : new InvalidOperationException("fallback down");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(sender));

        Assert.Same(primaryFailure, thrown);
        Assert.Equal(2, sender.Used.Count);
        Assert.Null(tracker.Get(SystemConfigServices.Smtp).ActiveSlot);
    }

    [Fact]
    public async Task CallerCancellation_IsRethrown_WithoutTryingTheFallback()
    {
        (RecordingSender sender, ServiceStatusTracker tracker) = Create(true);
        using (CancellationTokenSource source = new CancellationTokenSource())
        {
            source.Cancel();
            sender.Fail = settings => new TaskCanceledException("cancelled");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(sender, source.Token));

            Assert.Single(sender.Used);
        }
    }

    [Fact]
    public async Task FailureCodes_NameTheKindOfProblemWithoutDetails()
    {
        Dictionary<string, Exception> cases = new Dictionary<string, Exception>();
        cases["AUTH"] = new AuthenticationException("bad password primary-pass-1111");
        cases["NETWORK"] = new SocketException();
        cases["TIMEOUT"] = new TimeoutException();
        cases["ERROR"] = new InvalidOperationException("whatever");

        foreach (KeyValuePair<string, Exception> entry in cases)
        {
            (RecordingSender sender, ServiceStatusTracker tracker) = Create(false);
            sender.Fail = settings => entry.Value;

            await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(sender));

            Assert.Equal(entry.Key, tracker.Get(SystemConfigServices.Smtp).LastErrorCode);
        }
    }
}
