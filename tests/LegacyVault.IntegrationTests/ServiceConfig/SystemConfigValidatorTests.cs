using LegacyVault.Application.Exceptions;
using LegacyVault.Application.ServiceConfig;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>Luật kiểm tra giá trị cấu hình dịch vụ (Gemini): tên model đi vào URL nên bị giới hạn chặt.</summary>
public sealed class SystemConfigValidatorTests
{
    private static Dictionary<string, string?> ValidSettings()
    {
        Dictionary<string, string?> settings = new Dictionary<string, string?>();
        settings["model"] = "gemini-flash-lite-latest";
        settings["timeoutSeconds"] = "20";
        return settings;
    }

    private static void AssertValidationError(Action action)
    {
        AppException error = Assert.Throws<AppException>(action);
        Assert.Equal(AppErrorType.Validation, error.Type);
    }

    [Fact]
    public void Catalog_GeminiIsWiredAndSupportsFallback_OthersAreNotYet()
    {
        Assert.True(SystemConfigCatalog.IsWired(SystemConfigServices.Gemini));
        Assert.True(SystemConfigCatalog.SupportsFallback(SystemConfigServices.Gemini));
        Assert.False(SystemConfigCatalog.IsWired("NOPE"));
        Assert.Equal(new[] { "model", "timeoutSeconds", "apiKey" },
            SystemConfigCatalog.Fields(SystemConfigServices.Gemini).Select(f => f.Name).ToArray());
    }

    [Fact]
    public void ValidSettings_AreAccepted()
    {
        SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, ValidSettings());
    }

    [Theory]
    [InlineData("a/../b")]
    [InlineData("m?key=1")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("   ")]
    public void Model_RejectsPathInjectionAndBlank(string model)
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["model"] = model;

        AssertValidationError(() => SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, settings));
    }

    [Fact]
    public void Model_Over100Characters_IsRejected()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["model"] = new string('a', 101);

        AssertValidationError(() => SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, settings));
    }

    [Theory]
    [InlineData("gemini-flash-lite-latest")]
    [InlineData("gemini-2.5-flash")]
    public void Model_AcceptsRealModelNames(string model)
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["model"] = model;

        SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, settings);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("61")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("20.5")]
    public void TimeoutSeconds_MustBeIntegerFiveToSixty(string value)
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["timeoutSeconds"] = value;

        AssertValidationError(() => SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, settings));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("60")]
    public void TimeoutSeconds_AcceptsTheBounds(string value)
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["timeoutSeconds"] = value;

        SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, settings);
    }

    [Fact]
    public void UnknownSettingField_IsRejected()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["baseUrl"] = "http://x";

        AssertValidationError(() => SystemConfigValidator.ValidateSettings(SystemConfigServices.Gemini, settings));
    }

    [Fact]
    public void Secrets_AllowBlankApiKey_ButRejectUnknownNameAndTooLongValue()
    {
        Dictionary<string, string?> blank = new Dictionary<string, string?>();
        blank["apiKey"] = "";
        SystemConfigValidator.ValidateSecrets(SystemConfigServices.Gemini, blank);

        Dictionary<string, string?> unknown = new Dictionary<string, string?>();
        unknown["password"] = "x";
        AssertValidationError(() => SystemConfigValidator.ValidateSecrets(SystemConfigServices.Gemini, unknown));

        Dictionary<string, string?> tooLong = new Dictionary<string, string?>();
        tooLong["apiKey"] = new string('k', 513);
        AssertValidationError(() => SystemConfigValidator.ValidateSecrets(SystemConfigServices.Gemini, tooLong));
    }

    private static ConfigFieldSpec UrlField()
    {
        ConfigFieldSpec field = new ConfigFieldSpec();
        field.Name = "baseUrl";
        field.Kind = ConfigFieldKind.Url;
        return field;
    }

    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("http://127.0.0.1:8879")]
    [InlineData("http://localhost:8879/path")]
    public void Url_AcceptsHttpAndHttps(string value)
    {
        SystemConfigValidator.ValidateField(UrlField(), value, "settings.baseUrl");
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:pass@example.com")]
    [InlineData("not a url")]
    [InlineData("//example.com")]
    [InlineData("https://")]
    public void Url_RejectsOtherSchemesCredentialsAndGarbage(string value)
    {
        AssertValidationError(() => SystemConfigValidator.ValidateField(UrlField(), value, "settings.baseUrl"));
    }

    [Fact]
    public void Url_Over2048Characters_IsRejected()
    {
        string value = "https://example.com/" + new string('a', 2040);

        AssertValidationError(() => SystemConfigValidator.ValidateField(UrlField(), value, "settings.baseUrl"));
    }

    [Fact]
    public void Choice_AcceptsOnlyListedValuesExactly()
    {
        ConfigFieldSpec field = new ConfigFieldSpec();
        field.Name = "environment";
        field.Kind = ConfigFieldKind.Choice;
        field.Choices = new List<string> { "Test", "Live" };

        SystemConfigValidator.ValidateField(field, "Test", "settings.environment");
        SystemConfigValidator.ValidateField(field, "Live", "settings.environment");
        AssertValidationError(() => SystemConfigValidator.ValidateField(field, "live", "settings.environment"));
        AssertValidationError(() => SystemConfigValidator.ValidateField(field, "Prod", "settings.environment"));
    }

    private static Dictionary<string, string> Values(params string[] pairs)
    {
        Dictionary<string, string> values = new Dictionary<string, string>();
        for (int i = 0; i < pairs.Length; i += 2)
        {
            values[pairs[i]] = pairs[i + 1];
        }
        return values;
    }

    [Fact]
    public void RequiresConfirm_SePay_OnlyWhenSwitchingToLive()
    {
        Assert.True(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.SePay,
            Values("environment", "Test"), Values("environment", "Live")));
        Assert.False(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.SePay,
            Values("environment", "Live"), Values("environment", "Live")));
        Assert.False(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.SePay,
            Values("environment", "Live"), Values("environment", "Test")));
    }

    [Fact]
    public void RequiresConfirm_R2_WhenAccountOrBucketChanges()
    {
        Dictionary<string, string> before = Values("accountId", "acc1", "bucketName", "bucket1");

        Assert.True(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.R2, before,
            Values("accountId", "acc2", "bucketName", "bucket1")));
        Assert.True(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.R2, before,
            Values("accountId", "acc1", "bucketName", "bucket2")));
        Assert.False(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.R2, before,
            Values("accountId", "acc1", "bucketName", "bucket1")));
    }

    [Fact]
    public void RequiresConfirm_OtherServices_NeverRequire()
    {
        Assert.False(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.Gemini,
            Values("model", "a"), Values("model", "b")));
        Assert.False(SystemConfigCatalog.RequiresConfirm(SystemConfigServices.Smtp,
            Values("host", "a"), Values("host", "b")));
    }

    [Fact]
    public void Reason_IsRequired_TrimmedAndLimitedTo500()
    {
        Assert.Equal("đổi model", SystemConfigValidator.RequireReason("  đổi model  "));
        AssertValidationError(() => SystemConfigValidator.RequireReason(null));
        AssertValidationError(() => SystemConfigValidator.RequireReason("   "));
        AssertValidationError(() => SystemConfigValidator.RequireReason(new string('x', 501)));
        Assert.Equal(500, SystemConfigValidator.RequireReason(new string('x', 500)).Length);
    }
}
