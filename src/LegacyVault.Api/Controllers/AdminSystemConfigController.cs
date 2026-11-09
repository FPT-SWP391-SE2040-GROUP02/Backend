using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Cấu hình dịch vụ ngoài (eKYC, SMTP, SePay, R2) do Admin sửa trên web. Quyền kiểm từ DB trong service: không phải Admin đang hoạt
/// động thì 404 như khu vực không tồn tại (SEC-04). Response không bao giờ chứa khóa bí mật.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/system-config")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSystemConfigController : ControllerBase
{
    private readonly ISystemConfigService systemConfigService;

    public AdminSystemConfigController(ISystemConfigService systemConfigService)
    {
        this.systemConfigService = systemConfigService;
    }

    [HttpGet]
    [ProducesResponseType<SystemConfigListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SystemConfigListResponse> List(CancellationToken ct)
    {
        return systemConfigService.ListAsync(ct);
    }

    [HttpPut("{serviceKey}/{slot}")]
    [ProducesResponseType<SystemConfigSlotResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<SystemConfigSlotResponse> Update(
        string serviceKey, string slot, UpdateSystemConfigRequest request, CancellationToken ct)
    {
        return systemConfigService.UpdateAsync(serviceKey, slot, request, ct);
    }

    /// <summary>Thử kết nối bằng giá trị đang nhập, không lưu gì. Giới hạn 10 lần/phút theo địa chỉ IP.</summary>
    [HttpPost("{serviceKey}/{slot}/test")]
    [EnableRateLimiting("system-config-test")]
    [ProducesResponseType<TestSystemConfigResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<TestSystemConfigResponse> Test(
        string serviceKey, string slot, TestSystemConfigRequest request, CancellationToken ct)
    {
        return systemConfigService.TestAsync(serviceKey, slot, request, ct);
    }

    [HttpPost("{serviceKey}/{slot}/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(
        string serviceKey, string slot, ResetSystemConfigRequest request, CancellationToken ct)
    {
        await systemConfigService.ResetAsync(serviceKey, slot, request, ct);
        return NoContent();
    }
}
