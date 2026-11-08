using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Mảnh khóa Chủ kho giao cho Người thực thi: cấp lần đầu và cấp lại khi quên khóa.</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/executor-key")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class ExecutorKeyController(IExecutorKeyService service) : ControllerBase
{
    /// <summary>Trạng thái mảnh: đã cấp chưa, phiên bản, có cấp mới hoặc cấp lại được không. Không trả mảnh.</summary>
    [HttpGet]
    [ProducesResponseType<ExecutorKeyStatusResponse>(StatusCodes.Status200OK)]
    public Task<ExecutorKeyStatusResponse> Get(Guid vaultId, CancellationToken ct)
    {
        return service.GetStatusAsync(vaultId, ct);
    }

    /// <summary>Cấp mảnh lần đầu; mảnh gốc chỉ có trong response này. Đã cấp → 409 EXECUTOR_KEY_ALREADY_ISSUED.</summary>
    [RequireVaultPin]
    [HttpPost]
    [ProducesResponseType<ExecutorKeyIssuedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExecutorKeyIssuedResponse> Issue(Guid vaultId, CancellationToken ct)
    {
        return service.IssueAsync(vaultId, ct);
    }

    /// <summary>Quên khóa: cấp mảnh mới, mảnh cũ hết hiệu lực. Chưa cấp → 409 EXECUTOR_KEY_NOT_ISSUED.</summary>
    [RequireVaultPin]
    [HttpPost("reissue")]
    [ProducesResponseType<ExecutorKeyIssuedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExecutorKeyIssuedResponse> Reissue(Guid vaultId, CancellationToken ct)
    {
        return service.ReissueAsync(vaultId, ct);
    }
}
