using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Người thực thi xem và tải tài sản của kho mình phụ trách (docs/api-contract.md mục 6.15b). Chỉ Executor hiệu lực của đúng kho;
/// người khác nhận 404. Mỗi lần tải nội dung được ghi nhật ký.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/executor/vaults/{vaultId:guid}/assets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExecutorAssetsController(IExecutorAssetService assetService, ILogger<ExecutorAssetsController> logger)
    : ControllerBase
{
    /// <summary>Danh sách tài sản (chỉ thông tin mô tả). sortBy: createdAt (mặc định) | name.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<AssetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<AssetResponse>> List(Guid vaultId, [FromQuery] PaginationRequest pagination, CancellationToken ct)
    {
        return assetService.ListAsync(vaultId, pagination, ct);
    }

    /// <summary>Tải nội dung một tài sản; kiểm toàn vẹn trước byte đầu tiên, hỏng thì 500 CONTENT_INTEGRITY_FAILED không kèm byte nào.</summary>
    [HttpGet("{assetId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadContent(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await assetService.PrepareContentAsync(vaultId, assetId, ct);
        }
        catch (ContentIntegrityException)
        {
            // Chỉ log mã, không log nội dung hay tên tệp (OPS-04).
            logger.LogError("Nội dung tài sản không qua kiểm toàn vẹn: kho {VaultId}, tài sản {AssetId}", vaultId, assetId);
            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Nội dung tài sản không vượt qua kiểm tra toàn vẹn nên không thể tải.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }
        return new HandoverFileResult(download);
    }
}
