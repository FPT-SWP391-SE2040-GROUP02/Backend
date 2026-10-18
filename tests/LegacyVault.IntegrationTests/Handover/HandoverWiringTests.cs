using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;
using LegacyVault.IntegrationTests.Email;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Email mời, chọn bộ gửi, kiểm cấu hình lúc khởi động và log khởi động.</summary>
public sealed class HandoverWiringTests
{
    // 08:00 UTC = 15:00 giờ Việt Nam (UTC+7).
    private static readonly DateTimeOffset Deadline = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private const string Link = "http://localhost:5173/receive?token=abc";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new List<(LogLevel, string)>();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Add((logLevel, formatter(state, exception)));
        }
    }

    private static SmtpSettings SettingsFor(FakeSmtpServer server)
    {
        SmtpSettings settings = new SmtpSettings();
        settings.Host = "127.0.0.1";
        settings.Port = server.Port;
        settings.SecureSocket = "None";
        settings.FromAddress = "no-reply@legacyvault.test";
        settings.FromName = "LegacyVault";
        return settings;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values, string environmentName)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HandoverSettingsTests.TestHostEnvironment(environmentName));
        services.AddEmailServices(configuration);
        HandoverConfig.AddHandover(services, configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void InvitationEmail_ContainsNameLinkAndDeadline()
    {
        EmailContent content = HandoverInvitationEmail.Build("Nguyễn Văn An", Link, Deadline);

        Assert.Contains("Nguyễn Văn An", content.TextBody);
        Assert.Contains(Link, content.TextBody);
        Assert.Contains(Link, content.HtmlBody);
        Assert.Contains("15:00", content.TextBody);
        Assert.Contains("19/10/2026", content.TextBody);
    }

    [Fact]
    public void InvitationEmail_HtmlEncodesName()
    {
        EmailContent content = HandoverInvitationEmail.Build("<b>Hack</b> & Co", Link, Deadline);

        Assert.Contains("&lt;b&gt;Hack&lt;/b&gt;", content.HtmlBody);
        Assert.DoesNotContain("<b>Hack</b>", content.HtmlBody);
    }

    [Fact]
    public async Task SmtpSender_SendsLinkBuiltFromClientUrl()
    {
        using FakeSmtpServer server = new FakeSmtpServer();
        SmtpMailSender mailSender = new SmtpMailSender(Options.Create(SettingsFor(server)));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:ClientUrl"] = "http://localhost:5173/" })
            .Build();
        SmtpHandoverInvitationSender sender = new SmtpHandoverInvitationSender(mailSender, configuration);
        HandoverInvitation invitation = new HandoverInvitation
        {
            TurnId = Guid.NewGuid(),
            ToEmail = "an@test.local",
            FullName = "Nguyễn Văn An",
            Token = "tok-123",
            ResponseDeadline = Deadline
        };

        await sender.SendAsync(invitation, CancellationToken.None);

        byte[] raw = Assert.Single(server.ReceivedMessages);
        using MemoryStream stream = new MemoryStream(raw);
        MimeMessage message = MimeMessage.Load(stream);
        Assert.Equal("an@test.local", message.To.Mailboxes.Single().Address);
        Assert.Contains("http://localhost:5173/receive?token=tok-123", message.TextBody);
        Assert.DoesNotContain("//receive", message.TextBody);
    }

    [Fact]
    public async Task LogSender_PrintsLinkOnlyInDevelopment()
    {
        HandoverInvitation invitation = new HandoverInvitation
        {
            TurnId = Guid.NewGuid(), ToEmail = "an@test.local", FullName = "An", Token = "secret-token-xyz", ResponseDeadline = Deadline
        };

        CapturingLogger<LogHandoverInvitationSender> devLogger = new CapturingLogger<LogHandoverInvitationSender>();
        await new LogHandoverInvitationSender(devLogger, new HandoverSettingsTests.TestHostEnvironment("Development"))
            .SendAsync(invitation, CancellationToken.None);
        Assert.Contains("secret-token-xyz", string.Join("\n", devLogger.Lines.Select(l => l.Text)));

        CapturingLogger<LogHandoverInvitationSender> prodLogger = new CapturingLogger<LogHandoverInvitationSender>();
        // Ngoài Development không giả vờ đã gửi: ném lỗi để lượt giữ InvitationSentAt = null, và vẫn không lộ token.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LogHandoverInvitationSender(prodLogger, new HandoverSettingsTests.TestHostEnvironment("Production"))
                .SendAsync(invitation, CancellationToken.None));
        string prodText = string.Join("\n", prodLogger.Lines.Select(l => l.Text));
        Assert.DoesNotContain("secret-token-xyz", prodText);
        Assert.DoesNotContain("an@test.local", prodText);
    }

    [Fact]
    public void AddHandover_WithoutSmtp_UsesLogSender()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>(), "Development");

        IHandoverInvitationSender sender = provider.GetRequiredService<IHandoverInvitationSender>();

        Assert.IsType<LogHandoverInvitationSender>(sender);
    }

    [Fact]
    public void AddHandover_WithSmtp_UsesSmtpSender()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["Email:Smtp:UserName"] = "user@gmail.com",
            ["Email:Smtp:Password"] = "app-password"
        };
        using ServiceProvider provider = BuildProvider(values, "Development");

        IHandoverInvitationSender sender = provider.GetRequiredService<IHandoverInvitationSender>();

        Assert.IsType<SmtpHandoverInvitationSender>(sender);
    }

    [Fact]
    public void AddHandover_VerificationOffInProduction_FailsOptionsValidation()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?> { ["Handover:RequireIdentityVerification"] = "false" };
        using ServiceProvider provider = BuildProvider(values, "Production");

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HandoverSettings>>().Value);
    }

    [Fact]
    public void AddHandover_VerificationOffInDevelopment_IsAccepted()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?> { ["Handover:RequireIdentityVerification"] = "false" };
        using ServiceProvider provider = BuildProvider(values, "Development");

        Assert.False(provider.GetRequiredService<IOptions<HandoverSettings>>().Value.RequireIdentityVerification);
    }

    [Fact]
    public async Task StartupLogger_WarnsWhenVerificationIsBypassed_AndNeverLogsSecrets()
    {
        CapturingLogger<HandoverStartupLogger> logger = new CapturingLogger<HandoverStartupLogger>();
        HandoverSettings settings = new HandoverSettings { RequireIdentityVerification = false };

        await new HandoverStartupLogger(Options.Create(settings), logger).StartAsync(CancellationToken.None);

        (LogLevel level, string text) = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("Development", text);
    }

    [Fact]
    public async Task StartupLogger_VerificationRequired_LogsInformation()
    {
        CapturingLogger<HandoverStartupLogger> logger = new CapturingLogger<HandoverStartupLogger>();

        await new HandoverStartupLogger(Options.Create(new HandoverSettings()), logger).StartAsync(CancellationToken.None);

        (LogLevel level, string _) = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, level);
    }
}
