using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Tài sản số trong kho (docs/api-contract.md mục 6.7).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/assets")]
[Produces("application/json")]
public sealed class AssetsController(IAssetService assetService) : ControllerBase
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
}
