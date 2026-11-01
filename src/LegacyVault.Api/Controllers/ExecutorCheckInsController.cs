using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>Executor điểm danh thay chủ kho khi chủ kho quá hạn điểm danh.</summary>
/// <remarks>
/// Người gọi phải là Executor đang hiệu lực của kho và không phải chủ kho; không đủ điều kiện thì 404.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor")]
// Cố ý KHÔNG đặt [Produces("application/json")]: attribute đó ghi đè content-type problem+json của lỗi validate 400.
public sealed class ExecutorCheckInsController : ControllerBase
{
    // Dùng chung giới hạn tần suất với kênh điểm danh của chủ kho.
    private const string CheckInRateLimitPolicy = "check-ins";

    private readonly IExecutorCheckInService executorCheckInService;

    public ExecutorCheckInsController(IExecutorCheckInService executorCheckInService)
    {
        this.executorCheckInService = executorCheckInService;
    }

    /// <summary>
    /// "Điểm danh thay chủ kho": chỉ khi kế hoạch đang CHECKIN_OVERDUE hoặc AWAITING_CHECK và kho chưa có hồ sơ
    /// chứng tử đang mở. Đóng kỳ quá hạn, mở kỳ mới, đưa kế hoạch về ACTIVE.
    /// </summary>
    [HttpPost("vaults/{vaultId:guid}/check-in")]
    [EnableRateLimiting(CheckInRateLimitPolicy)]
    [ProducesResponseType<ExecutorCheckInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<ExecutorCheckInResponse> CheckInForOwner(Guid vaultId, CancellationToken ct)
    {
        return executorCheckInService.CheckInForOwnerAsync(vaultId, ct);
    }
}
