using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Mã nhận bàn giao: server tự tạo lúc kích hoạt; Chủ kho xem trạng thái và đổi mã.</summary>
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

    /// <summary>Đổi mã nhận: mã mới thay mã cũ, không trả mã gốc. Chưa có mã → 409 EXECUTOR_KEY_NOT_ISSUED.</summary>
    [RequireVaultPin]
    [HttpPost("reissue")]
    [ProducesResponseType<ExecutorKeyStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExecutorKeyStatusResponse> Reissue(Guid vaultId, CancellationToken ct)
    {
        return service.ReissueAsync(vaultId, ct);
    }
}

/// <summary>Cổng Người thực thi: đọc mã nhận bàn giao do Chủ kho gửi trực tiếp trên hệ thống.</summary>
[ApiController]
[Authorize]
[Route("api/v1/executor/vaults/{vaultId:guid}/handover-key")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class ExecutorHandoverKeyController(IExecutorKeyService service) : ControllerBase
{
    /// <summary>Đọc mã nhận bàn giao; chỉ được sau khi bàn giao đã bắt đầu (HandoverInProgress), chưa tới lúc → 409.</summary>
    [HttpGet]
    [ProducesResponseType<ExecutorHandoverKeyResponse>(StatusCodes.Status200OK)]
    public Task<ExecutorHandoverKeyResponse> Get(Guid vaultId, CancellationToken ct) =>
        service.GetKeyForExecutorAsync(vaultId, ct);
}
