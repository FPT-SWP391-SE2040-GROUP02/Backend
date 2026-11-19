using LegacyVault.Application.Exceptions;
using LegacyVault.Infrastructure.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.GoogleAuth;

/// <summary>Nonce đăng nhập Google do Backend phát: dùng một lần, hết hạn, không phình bộ nhớ. Không cần DB.</summary>
public sealed class GoogleNonceStoreTests
{
    private readonly FixedTimeProvider clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly GoogleNonceStore store;

    public GoogleNonceStoreTests()
    {
        store = new GoogleNonceStore(clock);
    }

    [Fact]
    public void IssuedNonce_CanBeConsumedExactlyOnce()
    {
        string nonce = store.Issue();

        Assert.True(store.TryConsume(nonce));
        Assert.False(store.TryConsume(nonce));
    }

    [Fact]
    public void IssuedNonces_AreDifferentAndUrlSafe()
    {
        string first = store.Issue();
        string second = store.Issue();

        Assert.NotEqual(first, second);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
        Assert.InRange(first.Length, 40, 256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong-do-backend-phat")]
    public void UnknownOrEmptyNonce_IsRejected(string nonce)
    {
        Assert.False(store.TryConsume(nonce));
    }

    [Fact]
    public void ExpiredNonce_IsRejected()
    {
        string nonce = store.Issue();
        clock.Now = clock.Now + GoogleNonceStore.Lifetime + TimeSpan.FromSeconds(1);

        Assert.False(store.TryConsume(nonce));
    }

    [Fact]
    public void WhenFullOfLiveNonces_IssueIsRefused_ButExpiredOnesAreReclaimed()
    {
        for (int i = 0; i < GoogleNonceStore.MaxPending; i++)
        {
            store.Issue();
        }

        Assert.Throws<AppException>(() => store.Issue());

        clock.Now = clock.Now + GoogleNonceStore.Lifetime + TimeSpan.FromSeconds(1);
        string fresh = store.Issue();
        Assert.True(store.TryConsume(fresh));
    }
}
