using LegacyVault.Application.ServiceConfig;
using LegacyVault.Infrastructure.Billing;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Đọc cấu hình SePay (PAY-01: môi trường Test và Live tách biệt) từ cấu hình đang có hiệu lực. Không cần database.</summary>
public sealed class BillingSePaySettingsTests
{
    private sealed class FakeReader : ISyncConfigReader
    {
        public ResolvedConfig? Config { get; set; }

        public ResolvedConfig? Read(string serviceKey)
        {
            return Config;
        }
    }

    private static BillingSePaySettings NewSettings(string environment, string? key)
    {
        ResolvedConfig config = new ResolvedConfig();
        config.Settings["environment"] = environment;
        if (key != null)
        {
            config.Secrets["webhookApiKey"] = key;
        }
        FakeReader reader = new FakeReader();
        reader.Config = config;
        return new BillingSePaySettings(reader);
    }

    [Fact]
    public void Pay01_Environment_DefaultIsTestAndKeyIsEmpty()
    {
        // Chuẩn bị: cấu hình mặc định của danh mục (chưa đặt môi trường, chưa có khóa)
        ResolvedConfig config = new ResolvedConfig();
        config.Settings = SystemConfigCatalog.DefaultSettings(SystemConfigServices.SePay);
        FakeReader reader = new FakeReader();
        reader.Config = config;
        BillingSePaySettings settings = new BillingSePaySettings(reader);

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

    [Fact]
    public void WebhookApiKey_ComesFromTheCurrentConfig_AndIsEmptyWhenMissing()
    {
        Assert.Equal("fake-key", NewSettings("Test", "fake-key").WebhookApiKey);
        Assert.Equal(string.Empty, NewSettings("Test", null).WebhookApiKey);
    }
}
