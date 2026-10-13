using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Sinh token đăng nhập và token ngẫu nhiên dùng một lần. Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Thời hạn phiên (idle 30 phút / tối đa 12 giờ, NFR-06) là luật nghiệp vụ, do service quyết định.
/// </remarks>
public interface ITokenService
{
    /// <summary>Access token ngắn hạn có chữ ký, chứa userId và role.</summary>
    AccessToken CreateAccessToken(User user);

    /// <summary>
    /// Chuỗi ngẫu nhiên an toàn (refresh token, link xác minh email, vé liên kết Google) kèm hash để lưu DB.
    /// </summary>
    OpaqueToken CreateOpaqueToken();

    /// <summary>Hash (SHA-256 hex) của token gốc FE gửi lên, để tìm trong DB.</summary>
    string HashOpaqueToken(string value);
}
