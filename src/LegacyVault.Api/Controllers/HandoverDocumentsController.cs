using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Biên bản đồng ý nhận và văn bản từ chối, nhìn từ phía người nhận.</summary>
/// <remarks>Chỉ người đã nhận lượt này; lượt của người khác hoặc id lạ cùng một lỗi 404 (SEC-04).</remarks>
[ApiController]
[Authorize]
[Route("api/v1/handover")]
[Produces("application/json")]
public sealed class HandoverDocumentsController : ControllerBase
{
    private readonly IHandoverDocumentService documentService;

    public HandoverDocumentsController(IHandoverDocumentService documentService)
    {
        this.documentService = documentService;
    }

    /// <summary>Bắt đầu biên bản: người nhận chọn Chấp nhận hoặc Từ chối, Phần 1 chép sẵn từ hồ sơ chứng tử, đến lượt người nhận điền Phần 2 và ký.</summary>
    [HttpPost("turns/{turnId:guid}/document")]
    [Consumes("application/json")]
    [ProducesResponseType<HandoverDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverDocumentResponse> Create(Guid turnId, CreateHandoverDocumentRequest request, CancellationToken ct)
    {
        return documentService.CreateAsync(turnId, request, ct);
    }

    /// <summary>Biên bản của lượt này; 204 khi chưa bắt đầu.</summary>
    [HttpGet("turns/{turnId:guid}/document")]
    [ProducesResponseType<HandoverDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HandoverDocumentResponse>> GetByTurn(Guid turnId, CancellationToken ct)
    {
        HandoverDocumentResponse? document = await documentService.GetByTurnAsync(turnId, ct);
        if (document == null)
        {
            return NoContent();
        }
        return document;
    }

    /// <summary>Mọi biên bản của tôi (mục Biên Bản), mới nhất trước, không giới hạn thời gian.</summary>
    [HttpGet("documents")]
    [ProducesResponseType<List<HandoverDocumentResponse>>(StatusCodes.Status200OK)]
    public Task<List<HandoverDocumentResponse>> ListMine(CancellationToken ct)
    {
        return documentService.ListMineAsync(ct);
    }
}
