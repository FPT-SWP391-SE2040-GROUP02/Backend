using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Handover;

/// <summary>
/// Kết quả tải tệp: chỉ được tạo SAU khi nội dung đã qua kiểm toàn vẹn (PreparedDownload).
/// Luôn trả dạng tải về, không cho trình duyệt tự hiển thị hay đoán kiểu nội dung.
/// </summary>
public sealed class HandoverFileResult : IActionResult
{
    private readonly PreparedDownload download;

    public HandoverFileResult(PreparedDownload download)
    {
        this.download = download;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/octet-stream";
        response.ContentLength = download.Length;
        response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(download.FileName);
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";

        await download.WriteToAsync(response.Body, context.HttpContext.RequestAborted);
    }
}
