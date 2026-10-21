using LegacyVault.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Đọc cấu hình Billing:SePay (PAY-01: môi trường Test và Live tách biệt). Không cần database.</summary>
public sealed class BillingSePaySettingsTests
{
    private static BillingSePaySettings NewSettings(string environment, string key)
    {
        BillingSettings billing = new BillingSettings();
        billing.SePay.Environment = environment;
        billing.SePay.WebhookApiKey = key;
        return new BillingSePaySettings(Options.Create(billing));
    }

    [Fact]
    public void Pay01_Environment_DefaultIsTestAndKeyIsEmpty()
    {
        // Chuẩn bị
        BillingSettings billing = new BillingSettings();
        BillingSePaySettings settings = new BillingSePaySettings(Options.Create(billing));

        // Hành động
        string environment = settings.Environment;
        string key = settings.WebhookApiKey;

        // Kiểm tra: chưa cấu hình gì thì là Test và chưa có key
        Assert.Equal("Test", environment);
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void Pay01_Environment_AcceptsTestAndLiveIgnoringCase()
    {
        // Chuẩn bị
        BillingSePaySettings live = NewSettings("live", "fake-key");
        BillingSePaySettings test = NewSettings("TEST", "fake-key");

        // Hành động
        string liveValue = live.Environment;
        string testValue = test.Environment;

        // Kiểm tra: trả dạng chuẩn
        Assert.Equal("Live", liveValue);
        Assert.Equal("Test", testValue);
    }

    [Fact]
    public void Pay01_Environment_UnknownValue_ThrowsConfigurationError()
    {
        // Chuẩn bị: gõ nhầm "Prod" không được âm thầm coi là Test
        BillingSePaySettings settings = NewSettings("Prod", "fake-key");

        // Hành động + Kiểm tra
        Assert.Throws<InvalidOperationException>(() => settings.Environment);
    }
}
