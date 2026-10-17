using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Owner xem hồ sơ chứng tử và báo "Tôi còn sống" (docs/api-contract.md mục 6.15).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/death-case")]
public sealed class OwnerObjectionController : ControllerBase
{
    private const long MaxRequestBytes = 10 * 1024 * 1024;

    private readonly IOwnerObjectionService objectionService;

    public OwnerObjectionController(IOwnerObjectionService objectionService)
    {
        this.objectionService = objectionService;
    }

    /// <summary>Hồ sơ chứng tử đang mở của kho và báo còn sống gần nhất (nếu có).</summary>
    [HttpGet]
    [ProducesResponseType<OwnerDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<OwnerDeathCaseResponse> Get(Guid vaultId, CancellationToken ct)
    {
        return objectionService.GetAsync(vaultId, ct);
    }

    /// <summary>"Tôi còn sống": chặn ngay hồ sơ, kế hoạch và grant. Báo lặp không đổi hạn.</summary>
    [HttpPost("objection")]
    [ProducesResponseType<OwnerDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<OwnerDeathCaseResponse> Report(Guid vaultId, CancellationToken ct)
    {
        return objectionService.ReportAsync(vaultId, ct);
    }

    /// <summary>Selfie trực tiếp để so với mẫu khuôn mặt của Owner.</summary>
    [HttpPost("objection/selfie")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<OwnerDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<OwnerDeathCaseResponse> SubmitSelfie(Guid vaultId, [FromForm] EkycImageForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            return await objectionService.SubmitSelfieAsync(vaultId, content, file.FileName, file.ContentType, ct);
        }
    }
}
