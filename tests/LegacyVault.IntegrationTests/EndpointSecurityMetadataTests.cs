using System.Reflection;
using LegacyVault.Api.Controllers;
using LegacyVault.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>Khóa cấu hình bảo vệ endpoint bằng attribute: vai trò và rate limit không bị gỡ nhầm. Không cần DB.</summary>
public sealed class EndpointSecurityMetadataTests
{
    [Fact]
    public void Register_IsRateLimited()
    {
        MethodInfo method = typeof(AuthController).GetMethod(nameof(AuthController.Register))!;

        EnableRateLimitingAttribute? limit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(limit);
        Assert.Equal(AuthRateLimitPolicies.Register, limit!.PolicyName);
    }

    [Fact]
    public void EkycController_OnlyForOwnerRoleAndRateLimitedPerUser()
    {
        AuthorizeAttribute authorize = typeof(EkycController).GetCustomAttribute<AuthorizeAttribute>()!;
        EnableRateLimitingAttribute limit = typeof(EkycController).GetCustomAttribute<EnableRateLimitingAttribute>()!;

        Assert.Equal("USER", authorize.Roles);
        Assert.Equal(AuthRateLimitPolicies.Ekyc, limit.PolicyName);
    }

    [Fact]
    public void EkycLivenessFrame_HasItsOwnHigherLimit()
    {
        MethodInfo method = typeof(EkycController).GetMethod(nameof(EkycController.SubmitLivenessFrame))!;

        EnableRateLimitingAttribute? limit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(limit);
        Assert.Equal(AuthRateLimitPolicies.EkycFrame, limit!.PolicyName);
        Assert.True(AuthRateLimitPolicies.EkycFramePermitLimit > AuthRateLimitPolicies.EkycPermitLimit);
    }
}
