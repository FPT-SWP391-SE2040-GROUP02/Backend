using LegacyVault.Application.Notifications;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

public sealed class EmailBounceStoreTests : IAsyncLifetime
{
    private ApiFactory? factory;

    public async Task InitializeAsync()
    {
        factory = ApiFactory.CreateForTempDatabase();
        if (factory != null)
        {
            await factory.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (factory != null)
        {
            await factory.DropAsync();
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Record_ThenFind_IsCaseInsensitiveAndOnlyReturnsBounced()
    {
        if (factory == null)
        {
            return; // không có SQL test
        }
        using IServiceScope scope = factory.Services.CreateScope();
        IEmailBounceStore store = scope.ServiceProvider.GetRequiredService<IEmailBounceStore>();

        await store.RecordAsync("Ghost@Legacyvault.vn", "5.4.4", DateTimeOffset.UtcNow, CancellationToken.None);
        await store.RecordAsync("ghost@legacyvault.vn", "5.1.1", DateTimeOffset.UtcNow, CancellationToken.None); // cập nhật, không trùng

        HashSet<string> found = await store.FindBouncedAsync(
            new[] { "GHOST@legacyvault.vn", "ok@gmail.com", " " }, CancellationToken.None);

        Assert.Equal(new[] { "ghost@legacyvault.vn" }, found.ToArray());
    }
}
