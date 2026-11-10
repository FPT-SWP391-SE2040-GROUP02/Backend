using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Biên bản của các kho mà tôi là Người thực thi: chỉ xem lại. Phần 1 về Chủ kho đã chép sẵn từ hồ sơ chứng tử, không ai điền thêm.</summary>
/// <remarks>Người gọi phải là Người thực thi đang hiệu lực của kho và không phải Owner; không đủ điều kiện thì 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor/handover-documents")]
[Produces("application/json")]
public sealed class ExecutorHandoverDocumentsController : ControllerBase
{
    private readonly IHandoverDocumentService documentService;

    public ExecutorHandoverDocumentsController(IHandoverDocumentService documentService)
    {
        this.documentService = documentService;
    }

    /// <summary>Mọi biên bản của các kho tôi đang làm Người thực thi, mới nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<List<HandoverDocumentResponse>>(StatusCodes.Status200OK)]
    public Task<List<HandoverDocumentResponse>> List(CancellationToken ct)
    {
        return documentService.ListForExecutorAsync(ct);
    }

    [HttpGet("{documentId:guid}")]
    [ProducesResponseType<HandoverDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverDocumentResponse> Get(Guid documentId, CancellationToken ct)
    {
        return documentService.GetForExecutorAsync(documentId, ct);
    }
}
