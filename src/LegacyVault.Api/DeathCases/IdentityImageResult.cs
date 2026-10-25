using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.DeathCases;

/// <summary>
/// Trả ảnh danh tính (mặt trước CCCD, selfie báo còn sống) đã qua kiểm toàn vẹn cho Verifier. Giữ kiểu ảnh thật
/// (JPEG/PNG/WebP do service đã nhận theo nội dung) để trình duyệt hiển thị; cấm đoán kiểu, chạy script và bộ nhớ đệm.
/// </summary>
public sealed class IdentityImageResult : IActionResult
{
    private readonly PreparedDownload download;

    public IdentityImageResult(PreparedDownload download)
    {
        this.download = download;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = download.ContentType;
        response.ContentLength = download.Length;
        response.Headers["Content-Disposition"] = "inline; filename=\"" + download.FileName + "\"";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        response.Headers["Cache-Control"] = "private, no-store";

        await download.WriteToAsync(response.Body, context.HttpContext.RequestAborted);
    }
}
