using LegacyVault.Application.Notifications;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Đọc họ tên Chủ kho và liên hệ Người thực thi trên SQL Server thật (cho thư mời nhận tài sản).</summary>
public sealed class HandoverContactReaderTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    [SqlFact]
    public async Task ReturnsOwnerNameAndAcceptedExecutorContact()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        using IServiceScope scope = factory.Services.CreateScope();
        IHandoverContactReader reader = scope.ServiceProvider.GetRequiredService<IHandoverContactReader>();
        HandoverContactInfo info = await reader.FindAsync(vault.VaultId, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(info.OwnerFullName));
        Assert.False(string.IsNullOrWhiteSpace(info.ExecutorFullName));
        Assert.False(string.IsNullOrWhiteSpace(info.ExecutorEmail));
    }

    [SqlFact]
    public async Task UnknownVault_ReturnsEmptyInfo()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IHandoverContactReader reader = scope.ServiceProvider.GetRequiredService<IHandoverContactReader>();

        HandoverContactInfo info = await reader.FindAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(string.Empty, info.OwnerFullName);
        Assert.Equal(string.Empty, info.ExecutorFullName);
    }
}
