using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.DeathCases;

/// <summary>
/// Kết quả xem một trang giấy chứng tử: chỉ được tạo SAU khi nội dung đã qua kiểm toàn vẹn (PreparedDeathCaseFile).
/// Luôn trả dạng tải về, không cho trình duyệt tự hiển thị hay đoán kiểu nội dung (DATA-06).
/// </summary>
public sealed class DeathCaseFileResult : IActionResult
{
    private readonly PreparedDeathCaseFile file;

    public DeathCaseFileResult(PreparedDeathCaseFile file)
    {
        this.file = file;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ResolveContentType(file.ContentType);
        response.ContentLength = file.Length;
        response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(file.FileName);
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";

        await file.WriteToAsync(response.Body, context.HttpContext.RequestAborted);
    }

    /// <summary>Chỉ 4 loại tệp được nhận lúc tải lên mới được giữ nguyên Content-Type; còn lại là octet-stream.</summary>
    private static string ResolveContentType(string contentType)
    {
        if (contentType == DeathCaseFileRules.Pdf
            || contentType == DeathCaseFileRules.Jpeg
            || contentType == DeathCaseFileRules.Png
            || contentType == DeathCaseFileRules.Webp)
        {
            return contentType;
        }
        return "application/octet-stream";
    }
}
