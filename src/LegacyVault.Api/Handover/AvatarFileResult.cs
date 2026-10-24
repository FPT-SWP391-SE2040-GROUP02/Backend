using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Handover;

/// <summary>
/// Trả ảnh đại diện đã qua kiểm toàn vẹn. Khác HandoverFileResult ở chỗ giữ kiểu ảnh thật (chỉ JPEG/PNG/WebP do
/// AvatarService đã nhận theo nội dung) để trình duyệt hiển thị; vẫn cấm đoán kiểu và không cho bộ nhớ đệm chung giữ ảnh.
/// </summary>
public sealed class AvatarFileResult : IActionResult
{
    private readonly PreparedDownload download;

    public AvatarFileResult(PreparedDownload download)
    {
        this.download = download;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = download.ContentType;
        response.ContentLength = download.Length;
        response.Headers["Content-Disposition"] = "inline; filename=\"avatar\"";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        response.Headers["Cache-Control"] = "private, no-store";

        await download.WriteToAsync(response.Body, context.HttpContext.RequestAborted);
    }
}
