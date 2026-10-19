using System.Reflection;
using LegacyVault.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Response chứa token phiên hoặc thông tin cá nhân phải có Cache-Control: no-store (API response review 18/10/2026, F1).
/// Kiểm ở cấp controller để controller mới trong nhóm này không bị bỏ sót chính sách.
/// </summary>
public sealed class NoStorePolicyTests
{
    [Theory]
    [InlineData(typeof(AuthController))]
    [InlineData(typeof(GoogleAuthController))]
    [InlineData(typeof(BeneficiariesController))]
    [InlineData(typeof(AdminController))]
    [InlineData(typeof(EkycController))]
    public void ControllerReturningTokensOrPersonalDataDeclaresNoStore(Type controller)
    {
        ResponseCacheAttribute? policy = controller.GetCustomAttribute<ResponseCacheAttribute>();

        Assert.NotNull(policy);
        Assert.True(policy.NoStore);
        Assert.Equal(ResponseCacheLocation.None, policy.Location);
    }
}
