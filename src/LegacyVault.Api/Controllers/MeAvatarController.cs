using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Ảnh đại diện của người đang đăng nhập (mọi vai trò, api-contract mục 6.4a).</summary>
[ApiController]
[Authorize]
[Route("api/v1/me/avatar")]
[Produces("application/json")]
public sealed class MeAvatarController(IAvatarService avatarService, ILogger<MeAvatarController> logger) : ControllerBase
{
    /// <summary>Phần dư cho boundary của multipart, ngoài nội dung ảnh.</summary>
    private const long MultipartOverheadBytes = 64 * 1024;
    private const long MaxRequestBytes = AvatarRules.MaxBytes + MultipartOverheadBytes;

    /// <summary>Tải ảnh của chính mình (nội dung ảnh, không phải JSON). 404 AVATAR_NOT_FOUND khi chưa có.</summary>
    [HttpGet]
    [Produces("image/jpeg", "image/png", "image/webp", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await avatarService.PrepareAsync(ct);
        }
        catch (ContentIntegrityException)
        {
            logger.LogError("Ảnh đại diện không qua kiểm toàn vẹn");

            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Ảnh đại diện không vượt qua kiểm tra toàn vẹn nên không thể tải.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        return new AvatarFileResult(download);
    }

    /// <summary>Đặt ảnh đại diện (multipart/form-data: file). JPEG, PNG hoặc WebP, tối đa 2 MiB. Trả người dùng mới.</summary>
    [HttpPut]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<UserResponse> Put([FromForm] AvatarForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using Stream content = file.OpenReadStream();
        return await avatarService.UploadAsync(content, file.Length, ct);
    }

    /// <summary>Xóa ảnh đại diện. Chưa có ảnh vẫn 200. Trả người dùng mới.</summary>
    [HttpDelete]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public Task<UserResponse> Delete(CancellationToken ct)
    {
        return avatarService.DeleteAsync(ct);
    }
}
