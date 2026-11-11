using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Biên bản của các kho mà tôi đã duyệt hồ sơ chứng tử, để Người xét duyệt kiểm tra và xem lại.</summary>
/// <remarks>Vai trò Verifier được service kiểm từ DB; kho không phải của mình không bao giờ hiện.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/handover-documents")]
[Produces("application/json")]
public sealed class VerifierHandoverDocumentsController : ControllerBase
{
    private readonly IHandoverDocumentService documentService;

    public VerifierHandoverDocumentsController(IHandoverDocumentService documentService)
    {
        this.documentService = documentService;
    }

    /// <summary>Mọi biên bản của các kho tôi đã duyệt chứng tử, mới nhất trước. Biên bản chờ tôi có status WAIT_VERIFIER.</summary>
    [HttpGet]
    [ProducesResponseType<List<HandoverDocumentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<List<HandoverDocumentResponse>> List(CancellationToken ct)
    {
        return documentService.ListForVerifierAsync(ct);
    }
}
