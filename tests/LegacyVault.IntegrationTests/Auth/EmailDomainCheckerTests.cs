using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

public sealed class EmailDomainCheckerTests
{
    private static DnsEmailDomainChecker Checker(bool enabled)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:DomainCheckEnabled"] = enabled.ToString() })
            .Build();
        return new DnsEmailDomainChecker(config);
    }

    [Fact]
    public async Task Disabled_AlwaysTrue()
    {
        Assert.True(await Checker(false).DomainExistsAsync("a@khong-ton-tai.invalid", CancellationToken.None));
    }

    [Fact]
    public async Task NoAtSign_IsLeftToFormatValidation()
    {
        Assert.True(await Checker(true).DomainExistsAsync("khong-co-a-cong", CancellationToken.None));
    }

    [Fact]
    public async Task ReservedInvalidTld_IsNotFound()
    {
        // .invalid được RFC 2606 dành riêng, DNS luôn trả NXDOMAIN; nếu máy không có mạng thì kết quả "cho qua" cũng chấp nhận.
        bool exists = await Checker(true).DomainExistsAsync("a@khong-ton-tai.invalid", CancellationToken.None);
        Assert.False(exists);
    }

    [Fact]
    public async Task Guard_ThrowsValidationErrorOnFieldWhenDomainMissing()
    {
        AppException ex = await Assert.ThrowsAsync<AppException>(() =>
            EmailDomainGuard.EnsureAsync(new MissingChecker(), "a@x.invalid", "email", CancellationToken.None));
        Assert.Equal(AppErrorType.Validation, ex.Type);
    }

    private sealed class MissingChecker : IEmailDomainChecker
    {
        public Task<bool> DomainExistsAsync(string email, CancellationToken ct) => Task.FromResult(false);
    }
}
