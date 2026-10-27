using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>Điểm danh định kỳ của Owner (docs/api-contract.md mục 6.8).</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
// Cố ý KHÔNG đặt [Produces("application/json")]: attribute đó ghi đè content-type problem+json của lỗi validate 400
// (ApiConfig.CreateValidationProblem), làm lỗi trả application/json.
public sealed class CheckInsController : ControllerBase
{
    // R5 (audit 2026-10-27): policy rate-limit riêng cho kênh điểm danh, chống flood.
    private const string CheckInRateLimitPolicy = "check-ins";

    // R2 (audit 2026-10-27): role claim trong JWT luôn THUA HỬA CHỮ HOA (UserRole.ToString().ToUpperInvariant());
    // "SmartwatchDevice" là role riêng của token thiết bị (JwtTokenService.SmartwatchRoleClaim).
    private const string OwnerRoles = "USER,VERIFIER,ADMIN,EXECUTOR";
    private const string DeviceRole = "SmartwatchDevice";

    private readonly ICheckInService checkInService;

    public CheckInsController(ICheckInService checkInService)
    {
        this.checkInService = checkInService;
    }

    /// <summary>
    /// Trạng thái điểm danh, các hạn, cấu hình và lịch sử (phân trang, kỳ mới nhất trước).
    /// Chưa kích hoạt kế hoạch → status NOT_STARTED. Đồng hồ cũng poll endpoint này để hiện trạng thái.
    /// </summary>
    [HttpGet("check-ins/me")]
    [Authorize(Roles = OwnerRoles + "," + DeviceRole)]
    [ProducesResponseType<CheckInStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CheckInStatusResponse> GetMyCheckIn([FromQuery] PaginationRequest pagination, CancellationToken ct)
    {
        return checkInService.GetMyCheckInAsync(pagination, ct);
    }

    /// <summary>"Điểm danh": đóng kỳ hiện tại, mở kỳ mới. Bấm lặp trong 60 giây trả trạng thái hiện tại.</summary>
    [HttpPost("check-ins")]
    [Authorize(Roles = OwnerRoles)]
    [EnableRateLimiting(CheckInRateLimitPolicy)]
    [ProducesResponseType<CheckInStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CheckInStatusResponse> CheckIn(CancellationToken ct)
    {
        return checkInService.CheckInAsync(ct);
    }

    /// <summary>
    /// Điểm danh sinh trắc học qua Smartwatch (Galaxy Watch): kiểm tra nhịp tim và trạng thái đeo trên tay.
    /// R2: chấp nhận cả token chủ kho (vai trò thật) lẫn token thiết bị "SmartwatchDevice" đã ghép nối.
    /// </summary>
    [HttpPost("check-ins/smartwatch")]
    [Authorize(Roles = OwnerRoles + "," + DeviceRole)]
    [EnableRateLimiting(CheckInRateLimitPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<CheckInStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CheckInStatusResponse> CheckInViaSmartwatch([FromBody] SmartwatchCheckInRequest request, CancellationToken ct)
    {
        return checkInService.CheckInViaSmartwatchAsync(request, ct);
    }

    /// <summary>Đổi chu kỳ (30/60/90 ngày) và thời gian chờ (7/14/30 ngày); áp dụng từ kỳ sau nếu đang có kỳ chạy.</summary>
    [HttpPatch("vaults/me/check-in-settings")]
    [Authorize(Roles = OwnerRoles)]
    [EnableRateLimiting(CheckInRateLimitPolicy)]
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
