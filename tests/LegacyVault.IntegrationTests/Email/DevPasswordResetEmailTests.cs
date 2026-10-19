using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

public sealed class DevPasswordResetEmailTests
{
    [Fact]
    public async Task ResetEmail_DoesNotLogRecoveryTokenOrPersonalData()
    {
        CapturingLogger logger = new();
        DevEmailSender sender = new(logger);
        await sender.SendPasswordResetAsync("private@example.test", "Private Person",
            "http://localhost/reset-password?token=secret-recovery-token", default);
        string log = Assert.Single(logger.Lines);
        Assert.Contains("not delivered", log);
        Assert.DoesNotContain("private@example.test", log);
        Assert.DoesNotContain("Private Person", log);
        Assert.DoesNotContain("secret-recovery-token", log);
        Assert.DoesNotContain("reset-password?", log);
    }

    private sealed class CapturingLogger : ILogger<DevEmailSender>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
