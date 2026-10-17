using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Verifier xem bằng chứng và kết luận báo còn sống (docs/api-contract.md mục 6.15).</summary>
[ApiController]
[Authorize]
[Route("api/v1/verifier/death-cases/{caseId:guid}/objection")]
public sealed class VerifierObjectionController : ControllerBase
{
    private readonly IVerifierObjectionService objectionService;

    public VerifierObjectionController(IVerifierObjectionService objectionService)
    {
        this.objectionService = objectionService;
    }

    [HttpGet]
    [ProducesResponseType<ObjectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ObjectionResponse> Get(Guid caseId, CancellationToken ct)
    {
        return objectionService.GetAsync(caseId, ct);
    }

    [HttpPost("conclude")]
    [Consumes("application/json")]
    [ProducesResponseType<ObjectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ObjectionResponse> Conclude(Guid caseId, ConcludeObjectionRequest request, CancellationToken ct)
    {
        return objectionService.ConcludeAsync(caseId, request, ct);
    }
}
