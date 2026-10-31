using System.Reflection;
using LegacyVault.Api.Controllers;
using LegacyVault.Api.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.Pins;

/// <summary>
/// Các action phải đòi mã PIN kho (docs/api-contract.md mục 6.20), kiểm ở cấp action nên chạy được khi không có
/// SQL Server. Phần nối HTTP (header, mã lỗi) nằm ở <see cref="VaultPinFlowTests"/>.
/// </summary>
public sealed class VaultPinCoverageTests
{
    [Theory]
    [InlineData(typeof(AssetsController), nameof(AssetsController.UpdateAsset))]
    [InlineData(typeof(VaultPinController), "Verify")]
    public void ActionRequiresVaultPin(Type controller, string action)
    {
        MethodInfo? method = controller.GetMethod(action);

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<RequireVaultPinAttribute>());
    }
}
