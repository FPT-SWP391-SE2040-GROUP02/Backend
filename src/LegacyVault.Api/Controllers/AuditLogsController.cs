using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Nhật ký hoạt động của kho, chỉ đọc (SEC-13; docs/api-contract.md mục 6.13).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/audit-logs")]
[Produces("application/json")]
public sealed class AuditLogsController(IAuditLogService auditLogService) : ControllerBase
{
    /// <summary>
    /// Một trang nhật ký của kho. sortBy: at (mặc định); isAscending=false để xem mới nhất trước.
    /// Lọc theo action (UPPER_SNAKE_CASE) và khoảng thời gian [from, to).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<AuditLogResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<AuditLogResponse>> GetAuditLogs(
        Guid vaultId, [FromQuery] PaginationRequest pagination, [FromQuery] AuditLogFilterRequest filter,
        CancellationToken ct)
    {
        return auditLogService.GetVaultAuditLogsAsync(vaultId, pagination, filter, ct);
    }
}
