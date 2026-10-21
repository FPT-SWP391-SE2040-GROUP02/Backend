using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Tài sản số trong kho (docs/api-contract.md mục 6.7).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/assets")]
[Produces("application/json")]
public sealed class AssetsController(IAssetService assetService, ILogger<AssetsController> logger) : ControllerBase
{
    /// <summary>Phần dư cho các field khác và boundary của multipart, ngoài nội dung tệp.</summary>
    private const long MultipartOverheadBytes = 1_048_576;
    private const long MaxRequestBytes = StorageLimits.MaxUploadBytes + MultipartOverheadBytes;

    /// <summary>Tải lên một tệp thành tài sản mới (multipart/form-data).</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<AssetResponse>> CreateFileAsset(
        Guid vaultId, [FromForm] CreateFileAssetForm form, CancellationToken ct)
    {
        //Kiểm tra file có hợp lệ không
        var file = form.File!;
        await using var content = file.OpenReadStream();
        var asset = await assetService.CreateFileAssetAsync(vaultId, new CreateFileAssetRequest
        {
            Name = form.Name,
            Description = form.Description,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Length = file.Length,
            Content = content,
        }, ct);
        return CreatedAtAction(nameof(GetAsset), new { vaultId, assetId = asset.Id }, asset);
    }

    /// <summary>Tạo tài sản dạng chữ (thông tin tài khoản, ghi chú...).</summary>
    [HttpPost("text")]
    [Consumes("application/json")]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> CreateTextAsset(
        Guid vaultId, CreateTextAssetRequest request, CancellationToken ct)
    {
        var asset = await assetService.CreateTextAssetAsync(vaultId, request, ct);
        return CreatedAtAction(nameof(GetAsset), new { vaultId, assetId = asset.Id }, asset);
    }

    /// <summary>Danh sách tài sản (metadata). sortBy: createdAt (mặc định) | name.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<AssetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<AssetResponse>> GetAssets(
        Guid vaultId, [FromQuery] PaginationRequest pagination, CancellationToken ct)
    {
        return assetService.GetAssetsAsync(vaultId, pagination, ct);
    }

    [HttpGet("{assetId:guid}")]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AssetResponse> GetAsset(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        return assetService.GetAssetAsync(vaultId, assetId, ct);
    }

    /// <summary>
    /// Owner tải nội dung tài sản của mình (phiên bản hiện tại, hoặc ?version=N). Nội dung được kiểm toàn vẹn trước khi
    /// gửi byte đầu tiên; nếu hỏng trả 500 CONTENT_INTEGRITY_FAILED mà không kèm byte nội dung nào.
    /// </summary>
    [HttpGet("{assetId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadContent(
        Guid vaultId, Guid assetId, [FromQuery] int? version, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await assetService.PrepareContentAsync(vaultId, assetId, version, ct);
        }
        catch (ContentIntegrityException)
        {
            // Chỉ log id, không log nội dung hay tên tệp (OPS-04).
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

    /// <summary>Đổi tên và/hoặc mô tả tài sản (không đổi nội dung).</summary>
    [HttpPut("{assetId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AssetResponse> UpdateAsset(
        Guid vaultId, Guid assetId, UpdateAssetRequest request, CancellationToken ct)
    {
        return assetService.UpdateAssetAsync(vaultId, assetId, request, ct);
    }

    /// <summary>Thay nội dung tài sản dạng tệp bằng một phiên bản mới (multipart/form-data: file, etag).</summary>
    [HttpPost("{assetId:guid}/versions")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<AssetResponse>> CreateFileVersion(
        Guid vaultId, Guid assetId, [FromForm] CreateFileAssetVersionForm form, CancellationToken ct)
    {
        var file = form.File!;
        await using var content = file.OpenReadStream();
        var asset = await assetService.CreateFileVersionAsync(vaultId, assetId, new CreateFileAssetVersionRequest
        {
            Etag = form.Etag ?? string.Empty,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Length = file.Length,
            Content = content,
        }, ct);
        return StatusCode(StatusCodes.Status201Created, asset);
    }

    /// <summary>Thay nội dung tài sản dạng chữ bằng một phiên bản mới.</summary>
    [HttpPost("{assetId:guid}/versions/text")]
    [Consumes("application/json")]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> CreateTextVersion(
        Guid vaultId, Guid assetId, CreateTextAssetVersionRequest request, CancellationToken ct)
    {
        var asset = await assetService.CreateTextVersionAsync(vaultId, assetId, request, ct);
        return StatusCode(StatusCodes.Status201Created, asset);
    }

    /// <summary>Xóa tài sản (chỉ khi không còn ở gói nào, chưa ghim vào bàn giao và kho không bị tạm giữ).</summary>
    [HttpDelete("{assetId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsset(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        await assetService.DeleteAssetAsync(vaultId, assetId, ct);
        return NoContent();
    }
}
