using LegacyVault.Application.Dtos;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Owner thu hồi lời mời hoặc Executor (docs/api-contract.md mục 6.13).</summary>
[ApiController, Authorize, Route("api/v1/vaults/{vaultId:guid}/executors")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ExecutorManagementController(IExecutorManagementService service) : ControllerBase
{
    /// <summary>Thu hồi. Gọi lặp trên dòng đã kết thúc trả trạng thái hiện tại.</summary>
    [HttpPost("{executorId:guid}/revoke")]
    [ProducesResponseType<ExecutorInvitationResponse>(StatusCodes.Status200OK)]
    public Task<ExecutorInvitationResponse> Revoke(
        Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct) =>
        service.RevokeAsync(vaultId, executorId, request, ct);
}
