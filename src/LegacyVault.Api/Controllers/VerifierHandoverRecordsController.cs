using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Luật sư lập và kiểm biên bản bàn giao theo đúng thứ tự: biên bản → danh tính người nhận → chữ ký ba bên.
/// Danh tính người nhận kiểm ở VerifierHandoverSubmissionsController.
/// </summary>
/// <remarks>Vai trò luật sư được service kiểm từ DB; biên bản không được giao cho mình luôn là 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/handover-records")]
[Produces("application/json")]
public sealed class VerifierHandoverRecordsController : ControllerBase
{
    private readonly IHandoverRecordService recordService;

    public VerifierHandoverRecordsController(IHandoverRecordService recordService)
    {
        this.recordService = recordService;
    }

    /// <summary>Các biên bản được giao cho tôi, mới nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<List<HandoverRecordSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<List<HandoverRecordSummaryResponse>> List(CancellationToken ct)
    {
        return recordService.ListForVerifierAsync(ct);
    }

    /// <summary>Một biên bản kèm tài sản, người nhận và file phân chia của Chủ kho để lập.</summary>
    [HttpGet("{recordId:guid}")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverRecordResponse> Get(Guid recordId, CancellationToken ct)
    {
        return recordService.GetForVerifierAsync(recordId, ct);
    }

    /// <summary>Lấy file phân chia của Chủ kho làm bản đầu (thay toàn bộ dòng đang có).</summary>
    [HttpPost("{recordId:guid}/use-owner-split")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> UseOwnerSplit(Guid recordId, CancellationToken ct)
    {
        return recordService.UseOwnerSplitAsync(recordId, ct);
    }

    /// <summary>Thay toàn bộ danh sách "ai nhận tài sản nào". Chỉ khi biên bản đang lập.</summary>
    [HttpPut("{recordId:guid}/lines")]
    [Consumes("application/json")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> SetLines(Guid recordId, SetHandoverRecordLinesRequest request, CancellationToken ct)
    {
        return recordService.SetLinesAsync(recordId, request, ct);
    }

    /// <summary>Chốt biên bản (xong bước 1): tạo lượt cho từng người nhận và gửi thư mời.</summary>
    [HttpPost("{recordId:guid}/finalize")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> Finalize(Guid recordId, CancellationToken ct)
    {
        return recordService.FinalizeAsync(recordId, ct);
    }

    /// <summary>Mở lại biên bản đã chốt để sửa. Mọi chữ ký cũ mất hiệu lực, phải ký lại từ đầu.</summary>
    [HttpPost("{recordId:guid}/reopen")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> Reopen(Guid recordId, CancellationToken ct)
    {
        return recordService.ReopenAsync(recordId, ct);
    }

    /// <summary>Luật sư ký xác minh biên bản (bước 3).</summary>
    [HttpPost("{recordId:guid}/sign")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> Sign(Guid recordId, CancellationToken ct)
    {
        return recordService.SignAsVerifierAsync(recordId, ct);
    }
}
