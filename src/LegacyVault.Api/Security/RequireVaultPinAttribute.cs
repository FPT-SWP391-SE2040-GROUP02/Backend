using System.Globalization;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LegacyVault.Api.Security;

/// <summary>
/// Đặt trên action cần mã PIN của kho (tải nội dung tài sản, xóa tài sản, đổi người nhận/Executor, xóa gói...).
/// Đọc <c>vaultId</c> trong route và PIN trong header <c>X-Vault-Pin</c>, rồi gọi
/// <see cref="IVaultPinService.VerifyAsync"/>. Route của action phải có tham số <c>vaultId</c>.
/// </summary>
/// <remarks>
/// Bộ lọc chạy sau [Authorize], nên kho của người khác vẫn trả 404 như các API khác. Token của đồng hồ thông minh
/// (có claim device_id) bị từ chối luôn, không cho nhập PIN.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireVaultPinAttribute : TypeFilterAttribute
{
    public RequireVaultPinAttribute() : base(typeof(RequireVaultPinFilter))
    {
    }
}

/// <summary>Bộ lọc thực sự của <see cref="RequireVaultPinAttribute"/>.</summary>
public sealed class RequireVaultPinFilter(IVaultPinService vaultPinService) : IAsyncActionFilter
{
    /// <summary>Tên header chứa mã PIN.</summary>
    public const string HeaderName = "X-Vault-Pin";

    private const string DeviceClaimType = "device_id";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (http.User.HasClaim(claim => claim.Type == DeviceClaimType))
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.RoleNotAllowed,
                "Thiết bị đeo không được thực hiện thao tác này.");
        }

        if (!context.RouteData.Values.TryGetValue("vaultId", out object? raw)
            || !Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out Guid vaultId))
        {
            throw new InvalidOperationException("[RequireVaultPin] cần tham số vaultId trong route.");
        }

        string? pin = http.Request.Headers[HeaderName].FirstOrDefault();
        await vaultPinService.VerifyAsync(vaultId, pin, http.RequestAborted);
        await next();
    }
}
