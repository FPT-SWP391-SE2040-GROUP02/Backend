using LegacyVault.Infrastructure.Billing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>D9: dev-pay chỉ bật ở Development và khi cấu hình bật; môi trường khác luôn tắt.</summary>
public sealed class BillingDevSettingsTests
{
    private static BillingDevSettings Create(string environmentName, bool configuredEnabled)
    {
        BillingSettings settings = new BillingSettings();
        settings.DevPaymentEnabled = configuredEnabled;
        return new BillingDevSettings(Options.Create(settings), new FakeEnvironment(environmentName));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void D9_NotDevelopment_DisabledEvenWhenConfiguredTrue(string environmentName)
    {
        // Chuẩn bị / Hành động
        BillingDevSettings settings = Create(environmentName, true);

        // Kiểm tra
        Assert.False(settings.DevPaymentEnabled);
    }

    [Fact]
    public void D9_Development_ConfiguredFalse_Disabled()
    {
        // Chuẩn bị / Hành động
        BillingDevSettings settings = Create(Environments.Development, false);

        // Kiểm tra
        Assert.False(settings.DevPaymentEnabled);
    }

    [Fact]
    public void D9_Development_ConfiguredTrue_Enabled()
    {
        // Chuẩn bị / Hành động
        BillingDevSettings settings = Create(Environments.Development, true);

        // Kiểm tra
        Assert.True(settings.DevPaymentEnabled);
    }

    [Fact]
    public void D9_DefaultSettings_DevPaymentIsOff()
    {
        // Chuẩn bị / Hành động: không cấu hình gì thì tắt
        BillingSettings defaults = new BillingSettings();

        // Kiểm tra
        Assert.False(defaults.DevPaymentEnabled);
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name)
        {
            EnvironmentName = name;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "LegacyVault.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
