using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Executor nộp hồ sơ chứng tử (docs/api-contract.md mục 6.10).</summary>
/// <remarks>
/// Người gọi phải là Executor đang hiệu lực của kho và không phải Owner; không đủ điều kiện thì 404 (SEC-04).
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor")]
// Cố ý KHÔNG đặt [Produces("application/json")]: attribute đó ghi đè content-type problem+json của lỗi validate 400.
public sealed class ExecutorDeathCasesController : ControllerBase
{
    /// <summary>NFR-09: tệp tối đa 20 MiB, cộng 1 MiB cho phần đầu multipart.</summary>
    private const long MaxRequestBytes = 21L * 1024 * 1024;

    private readonly IDeathCaseService deathCaseService;
    private readonly IExecutorCheckInService executorCheckInService;

    public ExecutorDeathCasesController(IDeathCaseService deathCaseService, IExecutorCheckInService executorCheckInService)
    {
        this.deathCaseService = deathCaseService;
        this.executorCheckInService = executorCheckInService;
    }

    /// <summary>DMS-05: các kho mà tôi là Executor đang hiệu lực, kèm cờ quá hạn điểm danh.</summary>
    [HttpGet("vaults")]
    [ProducesResponseType<List<ExecutorVaultResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<List<ExecutorVaultResponse>> GetMyVaults(CancellationToken ct)
    {
        return deathCaseService.GetMyVaultsAsync(ct);
    }

    /// <summary>DMS-05: điểm danh thay Owner khi Owner quá hạn và kho chưa có hồ sơ chứng tử đang mở.</summary>
    [HttpPost("vaults/{vaultId:guid}/check-in")]
    [ProducesResponseType<ExecutorCheckInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExecutorCheckInResponse> CheckInForOwner(Guid vaultId, CancellationToken ct)
    {
        return executorCheckInService.CheckInForOwnerAsync(vaultId, ct);
    }

    /// <summary>Mở hồ sơ chứng tử cho kho: 201 khi vừa tạo, 200 khi kho đã có hồ sơ đang mở.</summary>
    [HttpPost("vaults/{vaultId:guid}/death-case")]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeathCaseResponse>> OpenDeathCase(Guid vaultId, CancellationToken ct)
    {
        OpenDeathCaseResult result = await deathCaseService.OpenAsync(vaultId, ct);
        if (result.Created)
        {
            return CreatedAtAction(nameof(GetDeathCase), new { caseId = result.DeathCase.Id }, result.DeathCase);
        }
        return Ok(result.DeathCase);
    }

    [HttpGet("death-cases/{caseId:guid}")]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DeathCaseResponse> GetDeathCase(Guid caseId, CancellationToken ct)
    {
        return deathCaseService.GetAsync(caseId, ct);
    }

    /// <summary>Sửa bản nháp hiện tại; body có etag lấy từ lần đọc gần nhất. Chuỗi "" = xóa, không gửi = giữ nguyên.</summary>
    [HttpPut("death-cases/{caseId:guid}/draft")]
    [Consumes("application/json")]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<DeathCaseResponse> UpdateDraft(Guid caseId, UpdateDeathCaseDraftRequest request, CancellationToken ct)
    {
        return deathCaseService.UpdateDraftAsync(caseId, request, ct);
    }

    /// <summary>
    /// Tải lên một trang giấy chứng tử vào bản nháp (multipart/form-data, field <c>file</c>).
    /// PDF/JPEG/PNG/WEBP theo nội dung thật, ≤ 20 MiB, tối đa 10 tệp.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<DeathCaseResponse>> AddFile(
        Guid caseId, [FromForm] DeathCaseFileForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            AddDeathCaseFileRequest request = new AddDeathCaseFileRequest();
            request.FileName = file.FileName;
            request.Length = file.Length;
            request.Content = content;
            DeathCaseResponse response = await deathCaseService.AddFileAsync(caseId, request, ct);
            return CreatedAtAction(nameof(GetDeathCase), new { caseId }, response);
        }
    }

    /// <summary>
    /// Xác nhận và gửi bản hiện tại (body: etag, confirm = true). Gửi lặp trả trạng thái hiện tại.
    /// Lần gửi đầu khóa kế hoạch bàn giao của kho.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/submit")]
    [Consumes("application/json")]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<DeathCaseResponse> Submit(Guid caseId, SubmitDeathCaseRequest request, CancellationToken ct)
    {
        return deathCaseService.SubmitAsync(caseId, request, ct);
    }

    /// <summary>
    /// Tạo phiên bản bổ sung từ bản đã gửi (body: etag) khi hồ sơ đang xét hoặc cần bổ sung.
    /// Bản mới là bản nháp: sửa xong phải xác nhận gửi lại.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/versions")]
    [Consumes("application/json")]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeathCaseResponse>> CreateVersion(
        Guid caseId, CreateDeathCaseVersionRequest request, CancellationToken ct)
    {
        DeathCaseResponse response = await deathCaseService.CreateVersionAsync(caseId, request, ct);
        return CreatedAtAction(nameof(GetDeathCase), new { caseId }, response);
    }

    /// <summary>Gỡ một trang khỏi bản nháp. Bản đã gửi không gỡ được (409 DEATH_CASE_NOT_ALLOWED, VERSION_LOCKED).</summary>
    [HttpDelete("death-cases/{caseId:guid}/files/{fileId:guid}")]
    [ProducesResponseType<DeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<DeathCaseResponse> RemoveFile(Guid caseId, Guid fileId, CancellationToken ct)
    {
        return deathCaseService.RemoveFileAsync(caseId, fileId, ct);
    }
}
