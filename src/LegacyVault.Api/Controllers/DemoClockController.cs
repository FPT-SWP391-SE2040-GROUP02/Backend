using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Nút "Tua thời gian" để DEMO: đổi "hôm nay" của cả hệ thống sang một ngày khác và chạy việc nền ở từng mốc.
/// Chỉ chạy ở môi trường Development hoặc Staging; môi trường khác trả 404 như khu vực không tồn tại.
/// GET không cần đăng nhập (để giao diện hiện dải báo "đang giả lập ngày" ở mọi trang); tua và về ngày thật cần Admin.
/// </summary>
[ApiController]
[Route("api/v1/dev/clock")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DemoClockController : ControllerBase
{
    private readonly IDemoClockService demoClockService;
    private readonly IWebHostEnvironment environment;

    public DemoClockController(IDemoClockService demoClockService, IWebHostEnvironment environment)
    {
        this.demoClockService = demoClockService;
        this.environment = environment;
    }

    /// <summary>Giờ hệ thống đang dùng, giờ thật và độ lệch.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<DemoClockStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DemoClockStatusResponse>> GetClock(CancellationToken ct)
    {
        if (!IsAllowedEnvironment())
        {
            return NotFound();
        }
        return await demoClockService.GetStatusAsync(ct);
    }

    /// <summary>Tua giờ hệ thống tới 9:00 sáng (giờ Việt Nam) của ngày chọn, chạy việc nền ở từng mốc.</summary>
    [HttpPost("travel")]
    [Authorize]
    [Consumes("application/json")]
    [ProducesResponseType<DemoClockTravelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DemoClockTravelResponse>> Travel(DemoClockTravelRequest request, CancellationToken ct)
    {
        if (!IsAllowedEnvironment())
        {
            return NotFound();
        }
        return await demoClockService.TravelToAsync(request, ct);
    }

    /// <summary>Đưa giờ hệ thống về giờ thật. Dữ liệu đã sinh ra trong lúc tua vẫn giữ nguyên.</summary>
    [HttpPost("reset")]
    [Authorize]
    [ProducesResponseType<DemoClockStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DemoClockStatusResponse>> Reset(CancellationToken ct)
    {
        if (!IsAllowedEnvironment())
        {
            return NotFound();
        }
        return await demoClockService.ResetAsync(ct);
    }

    private bool IsAllowedEnvironment()
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Staging"))
        {
            return true;
        }
        return false;
    }
}
