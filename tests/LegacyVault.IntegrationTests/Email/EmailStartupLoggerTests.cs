using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>Dòng log lúc khởi động cho biết email đang gửi thật hay chỉ ghi log.</summary>
public sealed class EmailStartupLoggerTests
{
    private sealed class CapturingLogger : ILogger<EmailStartupLogger>
    {
        public List<string> Lines { get; } = new List<string>();

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
            Lines.Add(formatter(state, exception));
        }
    }

    private static async Task<string> StartAndReadLogAsync(SmtpSettings settings)
    {
        CapturingLogger logger = new CapturingLogger();
        EmailStartupLogger startupLogger = new EmailStartupLogger(Options.Create(settings), logger);

        await startupLogger.StartAsync(CancellationToken.None);

        return Assert.Single(logger.Lines);
    }

    [Fact]
    public async Task NotConfigured_SaysEmailsAreOnlyLoggedAndHowToFixIt()
    {
        string line = await StartAndReadLogAsync(new SmtpSettings());

        Assert.Contains("chưa cấu hình", line);
        Assert.Contains("Email:Smtp:UserName", line);
        Assert.Contains("Email:Smtp:Password", line);
    }

    [Fact]
    public async Task Configured_SaysEmailsGoThroughSmtpHostWithoutLeakingSecrets()
    {
        SmtpSettings settings = new SmtpSettings();
        settings.UserName = "secret.user@gmail.com";
        settings.Password = "super-secret-app-password";

        string line = await StartAndReadLogAsync(settings);

        Assert.Contains("smtp.gmail.com:587", line);
        Assert.DoesNotContain("super-secret-app-password", line);
        Assert.DoesNotContain("secret.user@gmail.com", line);
    }
}
