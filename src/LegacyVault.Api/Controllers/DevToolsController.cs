using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Công cụ thử nghiệm của Admin: tua thời gian của kho thử. Chỉ chạy ở môi trường Development; môi trường khác
/// trả 404 như khu vực không tồn tại. Quyền Admin kiểm từ DB trong service.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/dev-tools")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DevToolsController(IDevToolsService devToolsService, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Danh sách kho để chọn hồ sơ thử.</summary>
    [HttpGet("vaults")]
    [ProducesResponseType<List<DevVaultOptionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DevVaultOptionResponse>>> ListVaults(CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }
        return await devToolsService.ListVaultsAsync(ct);
    }

    /// <summary>Trạng thái kế hoạch, điểm danh, đồng hồ và các mốc tua được của một kho.</summary>
    [HttpGet("vaults/{vaultId:guid}/clock")]
    [ProducesResponseType<DevClockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DevClockResponse>> GetClock(Guid vaultId, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }
        return await devToolsService.GetClockAsync(vaultId, ct);
    }

    /// <summary>Tua kho tới một mốc thời gian.</summary>
    [HttpPost("vaults/{vaultId:guid}/time-travel")]
    [Consumes("application/json")]
    [ProducesResponseType<DevClockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DevClockResponse>> TimeTravel(
        Guid vaultId, DevTimeTravelRequest request, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }
        return await devToolsService.TimeTravelAsync(vaultId, request, ct);
    }

    /// <summary>Đưa đồng hồ của kho về thời gian thực; trạng thái đã phát sinh giữ nguyên.</summary>
    [HttpPost("vaults/{vaultId:guid}/reset-clock")]
    [ProducesResponseType<DevClockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DevClockResponse>> ResetClock(Guid vaultId, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }
        return await devToolsService.ResetClockAsync(vaultId, ct);
    }
}
