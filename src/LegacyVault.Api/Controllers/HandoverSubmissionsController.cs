using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Người nhận nộp hồ sơ (Chấp nhận hoặc Từ chối kèm ảnh căn cước) và xem trạng thái hồ sơ của lượt mình.</summary>
/// <remarks>Chỉ người đã nhận lượt này; lượt của người khác hoặc id lạ cùng một lỗi 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/handover/turns/{turnId:guid}/submission")]
public sealed class HandoverSubmissionsController : ControllerBase
{
    /// <summary>Hai ảnh tối đa 8 MiB mỗi ảnh cộng phần đầu multipart.</summary>
    private const long MaxRequestBytes = 17 * 1024 * 1024;

    private readonly IHandoverSubmissionService submissionService;

    public HandoverSubmissionsController(IHandoverSubmissionService submissionService)
    {
        this.submissionService = submissionService;
    }

    /// <summary>Nộp hồ sơ. Chỉ khi lượt ở bước xác minh danh tính; nộp lại thì bản cũ bị thay thế.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<HandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<HandoverSubmissionResponse> Submit(
        Guid turnId, [FromForm] SubmitHandoverSubmissionForm form, CancellationToken ct)
    {
        byte[] front = await ReadAsync(form.FrontImage!, ct);
        byte[] back = await ReadAsync(form.BackImage!, ct);
        return await submissionService.SubmitAsync(turnId, form, front, back, ct);
    }

    /// <summary>Hồ sơ mới nhất của lượt này (trạng thái, lý do cần bổ sung); 204 khi chưa nộp.</summary>
    [HttpGet]
    [ProducesResponseType<HandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HandoverSubmissionResponse>> GetLatest(Guid turnId, CancellationToken ct)
    {
        HandoverSubmissionResponse? latest = await submissionService.GetLatestAsync(turnId, ct);
        if (latest == null)
        {
            return NoContent();
        }
        return latest;
    }

    /// <summary>Đọc một ảnh tải lên vào bộ nhớ; ảnh lớn hơn giới hạn thì trả 413.</summary>
    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > EkycRules.MaxImageBytes)
        {
            throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge,
                "Ảnh căn cước quá lớn. Vui lòng chọn ảnh nhỏ hơn 8 MiB.");
        }
        await using (Stream content = file.OpenReadStream())
        using (MemoryStream buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
    }
}
