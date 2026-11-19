using Microsoft.AspNetCore.HttpOverrides;

namespace LegacyVault.Api.Config;

/// <summary>
/// Cấu hình tin X-Forwarded-For / X-Forwarded-Proto của reverse proxy (Render, Vercel).
/// </summary>
/// <remarks>
/// Mặc định ASP.NET Core chỉ tin header từ loopback. Sau proxy của Render thì kết nối đến từ IP của proxy,
/// header bị bỏ qua và mọi người dùng cùng một IP: rate limit và đếm đăng nhập sai gộp chung, kẻ xấu có thể khóa nạn nhân.
/// Bật <c>ForwardedHeaders:TrustAnyProxy=true</c> chỉ khi API chỉ nhận request qua proxy (Render). ForwardLimit giữ mặc định 1:
/// lấy địa chỉ ngoài cùng bên phải do proxy thêm, bỏ qua phần client tự gắn thêm phía trước.
/// </remarks>
public static class ForwardedHeadersConfig
{
    public const string TrustAnyProxyKey = "ForwardedHeaders:TrustAnyProxy";

    public static ForwardedHeadersOptions Create(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        if (configuration.GetValue<bool>(TrustAnyProxyKey))
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        }
        return options;
    }
}
