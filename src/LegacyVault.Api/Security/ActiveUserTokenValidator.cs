using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LegacyVault.Api.Security;

/// <summary>
/// Chạy sau khi JWT đã qua kiểm chữ ký và hạn: đọc lại trạng thái tài khoản trong DB ở mỗi request.
/// </summary>
/// <remarks>
/// - Tài khoản không còn: coi như chưa đăng nhập, endpoint [Authorize] trả 401 SESSION_EXPIRED.
/// - Tài khoản bị khóa hoặc vô hiệu hóa: endpoint [Authorize] trả 403 ACCOUNT_NOT_ACTIVE (ApiConfig đọc cờ InactiveAccountItem).
/// - Endpoint công khai vẫn chạy như người chưa đăng nhập.
/// </remarks>
public static class ActiveUserTokenValidator
{
    /// <summary>Khóa trong HttpContext.Items đánh dấu token thuộc tài khoản không còn hoạt động.</summary>
    public const string InactiveAccountItem = "LegacyVault.InactiveAccount";

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        string? subject = null;
        if (context.Principal != null)
        {
            subject = context.Principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }

        if (!Guid.TryParse(subject, out Guid userId))
        {
            context.Fail("Token không có mã tài khoản hợp lệ.");
            return;
        }

        IUserStatusReader statusReader = context.HttpContext.RequestServices.GetRequiredService<IUserStatusReader>();
        UserStatus? status = await statusReader.FindStatusAsync(userId, context.HttpContext.RequestAborted);
        if (status == null)
        {
            context.Fail("Tài khoản không còn tồn tại.");
            return;
        }

        if (status.Value != UserStatus.Active)
        {
            context.HttpContext.Items[InactiveAccountItem] = true;
            context.Fail("Tài khoản không còn hoạt động.");
        }
    }
}
