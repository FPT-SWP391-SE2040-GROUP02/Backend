using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Công tắc bỏ qua xác minh danh tính: mặc định bật, chỉ tắt được ở Development.</summary>
public sealed class HandoverSettingsTests
{
    [Fact]
    public void Defaults_RequireVerification()
    {
        Assert.True(new HandoverSettings().RequireIdentityVerification);

        HandoverJobSettings job = new HandoverJobSettings();
        Assert.True(job.Enabled);
        Assert.Equal(60, job.PollSeconds);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Validator_VerificationOn_AllowedInEveryEnvironment(string environmentName)
    {
        HandoverSettingsValidator validator = new HandoverSettingsValidator(new TestHostEnvironment(environmentName));

        ValidateOptionsResult result = validator.Validate(null, new HandoverSettings { RequireIdentityVerification = true });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_VerificationOff_AllowedInDevelopment()
    {
        HandoverSettingsValidator validator = new HandoverSettingsValidator(new TestHostEnvironment("Development"));

        ValidateOptionsResult result = validator.Validate(null, new HandoverSettings { RequireIdentityVerification = false });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("")]
    public void Validator_VerificationOff_FailsOutsideDevelopment(string environmentName)
    {
        HandoverSettingsValidator validator = new HandoverSettingsValidator(new TestHostEnvironment(environmentName));

        ValidateOptionsResult result = validator.Validate(null, new HandoverSettings { RequireIdentityVerification = false });

        Assert.True(result.Failed);
        Assert.Contains("Development", result.FailureMessage);
    }

    [Fact]
    public void Policy_ReflectsSettings()
    {
        HandoverPolicy on = new HandoverPolicy(Options.Create(new HandoverSettings { RequireIdentityVerification = true }));
        HandoverPolicy off = new HandoverPolicy(Options.Create(new HandoverSettings { RequireIdentityVerification = false }));

        Assert.True(on.RequireIdentityVerification);
        Assert.False(off.RequireIdentityVerification);
    }

    internal sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "LegacyVault.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
