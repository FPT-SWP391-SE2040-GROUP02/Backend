using LegacyVault.Api.Controllers;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

public sealed class PasswordResetFailureTests
{
    [Fact]
    public async Task Forgot_DeliveryFailureKeepsNeutral202WithoutLoggingSecrets()
    {
        CapturingLogger logger = new();
        AuthController controller = new(null!, null!, null!,
            new FailingResetService(new IOException("private@example.test token=secret")));
        IActionResult result = await controller.ForgotPassword(
            new ForgotPasswordRequest { Email = "private@example.test" }, logger, default);
        Assert.IsType<AcceptedResult>(result);
        string line = Assert.Single(logger.Lines);
        Assert.Contains("IOException", line);
        Assert.DoesNotContain("private@example.test", line);
        Assert.DoesNotContain("secret", line);
    }

    [Fact]
    public async Task Forgot_RequestCancellationIsNotSwallowed()
    {
        AuthController controller = new(null!, null!, null!, new FailingResetService(new OperationCanceledException()));
        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.ForgotPassword(
            new ForgotPasswordRequest { Email = "fixture@example.test" }, new CapturingLogger(), default));
    }

    private sealed class FailingResetService(Exception error) : IPasswordResetService
    {
        public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct) => Task.FromException(error);
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<AuthController>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
