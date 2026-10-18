using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Điểm danh định kỳ của Owner (docs/api-contract.md mục 6.8).</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
// Cố ý KHÔNG đặt [Produces("application/json")]: attribute đó ghi đè content-type problem+json của lỗi validate 400
// (ApiConfig.CreateValidationProblem), làm lỗi trả application/json.
public sealed class CheckInsController : ControllerBase
{
    private readonly ICheckInService checkInService;

    public CheckInsController(ICheckInService checkInService)
    {
        this.checkInService = checkInService;
    }

    /// <summary>
    /// Trạng thái điểm danh, các hạn, cấu hình và lịch sử (phân trang, kỳ mới nhất trước).
    /// Chưa kích hoạt kế hoạch → status NOT_STARTED.
    /// </summary>
    [HttpGet("check-ins/me")]
    [ProducesResponseType<CheckInStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CheckInStatusResponse> GetMyCheckIn([FromQuery] PaginationRequest pagination, CancellationToken ct)
    {
        return checkInService.GetMyCheckInAsync(pagination, ct);
    }

    /// <summary>"Điểm danh": đóng kỳ hiện tại, mở kỳ mới. Bấm lặp trong 60 giây trả trạng thái hiện tại.</summary>
    [HttpPost("check-ins")]
    [ProducesResponseType<CheckInStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CheckInStatusResponse> CheckIn(CancellationToken ct)
    {
        return checkInService.CheckInAsync(ct);
    }

    /// <summary>Đổi chu kỳ (30/60/90 ngày) và thời gian chờ (7/14/30 ngày); áp dụng từ kỳ sau nếu đang có kỳ chạy.</summary>
    [HttpPatch("vaults/me/check-in-settings")]
    [Consumes("application/json")]
    [ProducesResponseType<CheckInStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CheckInStatusResponse> UpdateSettings(UpdateCheckInSettingsRequest request, CancellationToken ct)
    {
        return checkInService.UpdateSettingsAsync(request, ct);
    }
}
