using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Executor theo dõi tiến độ bàn giao của hồ sơ chứng tử (docs/api-contract.md, mục tiến độ bàn giao).</summary>
/// <remarks>
/// Controller riêng cùng tiền tố với ExecutorDeathCasesController để không sửa controller đó.
/// Người gọi phải là Executor đang hiệu lực của kho và không phải Owner; không đủ điều kiện thì 404.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor")]
[Produces("application/json")]
public sealed class ExecutorHandoverController : ControllerBase
{
    private readonly IHandoverProgressService handoverProgressService;

    public ExecutorHandoverController(IHandoverProgressService handoverProgressService)
    {
        this.handoverProgressService = handoverProgressService;
    }

    /// <summary>
    /// Trạng thái từng lượt bàn giao và từng tài sản đã tải hay chưa. Không có nội dung tài sản
    /// và thông tin liên hệ người nhận. Hồ sơ chưa duyệt trả handoverStarted = false.
    /// </summary>
    [HttpGet("death-cases/{caseId:guid}/handover-progress")]
    [ProducesResponseType<HandoverProgressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverProgressResponse> GetHandoverProgress(Guid caseId, CancellationToken ct)
    {
        return handoverProgressService.GetProgressAsync(caseId, ct);
    }
}
