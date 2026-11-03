using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>Cấu hình dịch vụ đọc lúc chạy: dòng DB đè appsettings, ô dự phòng chỉ có khi bật, cache 30 giây xóa được.</summary>
public sealed class SystemConfigResolverTests : IAsyncLifetime
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

    private async Task AddRowAsync(SystemConfigSlot slot, bool enabled, string model, string apiKey)
    {
        SystemConfig row = new SystemConfig();
        row.ServiceKey = SystemConfigServices.Gemini;
        row.Slot = slot;
        row.Enabled = enabled;
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["model"] = model;
        settings["timeoutSeconds"] = "30";
        row.SettingsJson = JsonSerializer.Serialize(settings);
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        secrets["apiKey"] = apiKey;
        factory.Services.GetRequiredService<ISystemConfigSecretProtector>().Protect(row, secrets);
        row.Version = 1;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = Guid.NewGuid();
        row.UpdateReason = "test";
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.SystemConfigs.Add(row);
            await db.SaveChangesAsync();
        });
    }

    private static async Task<ResolvedConfig?> ResolveAsync(
        IServiceProvider services, SystemConfigSlot slot)
    {
        using (IServiceScope scope = services.CreateScope())
        {
            ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
            return await resolver.ResolveAsync(SystemConfigServices.Gemini, slot, CancellationToken.None);
        }
    }

    private static void Invalidate(IServiceProvider services)
    {
        using (IServiceScope scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>().Invalidate(SystemConfigServices.Gemini);
        }
    }

    [SqlFact]
    public async Task NoRow_PrimaryComesFromAppSettings()
    {
        using (WebApplicationFactory<Program> withKey = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                Dictionary<string, string?> values = new Dictionary<string, string?>();
                values["Gemini:ApiKey"] = "env-key";
                values["Gemini:Model"] = "gemini-2.5-flash";
                configuration.AddInMemoryCollection(values);
            });
        }))
        {
            ResolvedConfig? config = await ResolveAsync(withKey.Services, SystemConfigSlot.Primary);

            Assert.NotNull(config);
            Assert.Equal(ConfigSources.AppSettings, config.Source);
            Assert.Equal("gemini-2.5-flash", config.Settings["model"]);
            Assert.Equal("20", config.Settings["timeoutSeconds"]);
            Assert.Equal("env-key", config.Secrets["apiKey"]);
        }
    }

    [SqlFact]
    public async Task NoRowAndNoAppSettingsKey_PrimaryUsesCatalogDefaultsWithoutKey()
    {
        ResolvedConfig? config = await ResolveAsync(factory.Services, SystemConfigSlot.Primary);

        Assert.NotNull(config);
        Assert.Equal(ConfigSources.AppSettings, config.Source);
        Assert.Equal("gemini-flash-lite-latest", config.Settings["model"]);
        Assert.False(config.Secrets.ContainsKey("apiKey"));
    }

    [SqlFact]
    public async Task Row_OverridesAppSettings()
    {
        await AddRowAsync(SystemConfigSlot.Primary, true, "row-model", "row-key");

        ResolvedConfig? config = await ResolveAsync(factory.Services, SystemConfigSlot.Primary);

        Assert.NotNull(config);
        Assert.Equal(ConfigSources.Database, config.Source);
        Assert.Equal("row-model", config.Settings["model"]);
        Assert.Equal("30", config.Settings["timeoutSeconds"]);
        Assert.Equal("row-key", config.Secrets["apiKey"]);
    }

    [SqlFact]
    public async Task Row_MissingSetting_GetsCatalogDefault()
    {
        await AddRowAsync(SystemConfigSlot.Primary, true, "row-model", "row-key");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            SystemConfig row = await db.SystemConfigs.SingleAsync();
            row.SettingsJson = "{}";
            await db.SaveChangesAsync();
        });

        ResolvedConfig? config = await ResolveAsync(factory.Services, SystemConfigSlot.Primary);

        Assert.NotNull(config);
        Assert.Equal("gemini-flash-lite-latest", config.Settings["model"]);
    }

    [SqlFact]
    public async Task Fallback_WithoutRow_IsNull()
    {
        Assert.Null(await ResolveAsync(factory.Services, SystemConfigSlot.Fallback));
    }

    [SqlFact]
    public async Task Fallback_DisabledRow_IsNull()
    {
        await AddRowAsync(SystemConfigSlot.Fallback, false, "fb-model", "fb-key");

        Assert.Null(await ResolveAsync(factory.Services, SystemConfigSlot.Fallback));
    }

    [SqlFact]
    public async Task Fallback_EnabledRow_IsResolved()
    {
        await AddRowAsync(SystemConfigSlot.Fallback, true, "fb-model", "fb-key");

        ResolvedConfig? config = await ResolveAsync(factory.Services, SystemConfigSlot.Fallback);

        Assert.NotNull(config);
        Assert.Equal("fb-key", config.Secrets["apiKey"]);
    }

    [SqlFact]
    public async Task Cache_ServesOldValueUntilInvalidate()
    {
        await AddRowAsync(SystemConfigSlot.Primary, true, "old-model", "k");
        ResolvedConfig? first = await ResolveAsync(factory.Services, SystemConfigSlot.Primary);
        Assert.Equal("old-model", first!.Settings["model"]);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            SystemConfig row = await db.SystemConfigs.SingleAsync();
            row.SettingsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["model"] = "new-model" });
            await db.SaveChangesAsync();
        });

        ResolvedConfig? stale = await ResolveAsync(factory.Services, SystemConfigSlot.Primary);
        Assert.Equal("old-model", stale!.Settings["model"]);

        Invalidate(factory.Services);

        ResolvedConfig? fresh = await ResolveAsync(factory.Services, SystemConfigSlot.Primary);
        Assert.Equal("new-model", fresh!.Settings["model"]);
    }
}
