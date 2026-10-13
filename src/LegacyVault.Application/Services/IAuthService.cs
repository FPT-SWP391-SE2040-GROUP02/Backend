using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đăng nhập bằng email + mật khẩu, làm mới phiên, đăng xuất, xem phiên hiện tại.
/// </summary>
/// <remarks>
/// Luật chính khi cài đặt:
/// - SEC-10: 5 lần sai trong 15 phút theo cặp email + dải IP thì khóa 15 phút; email không tồn tại cũng đếm
///   và vẫn chạy băm giả để không lộ email đã đăng ký qua thời gian phản hồi.
/// - Kiểm tra trạng thái tài khoản SAU khi mật khẩu đúng (người lạ không dò được trạng thái).
/// - NFR-06: phiên idle 30 phút, tối đa 12 giờ từ lúc đăng nhập (kể cả "Ghi nhớ đăng nhập"); kiểm tra ở server.
/// - Refresh token xoay vòng; dùng lại token đã thu hồi → thu hồi cả family.
/// - Đăng nhập không tự tạo kho, không hoàn tất eKYC/điểm danh (§5.1 bước 5).
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Đăng nhập. ipRange = dải IP của người gọi (controller tính), dùng cho SEC-10.
    /// Lỗi: 401 INVALID_CREDENTIALS (+attemptsRemaining), 423 ACCOUNT_LOCKED (+remainingSeconds),
    /// 403 ACCOUNT_NOT_ACTIVE. Tài khoản không có mật khẩu app (chỉ Google) → 401 INVALID_CREDENTIALS.
    /// </summary>
    Task<AuthResult> LoginAsync(LoginRequest request, string ipRange, CancellationToken ct);

    /// <summary>
    /// Đổi refresh token (từ cookie) lấy cặp token mới.
    /// Lỗi: 401 SESSION_EXPIRED (thiếu/sai/hết hạn/đã thu hồi/quá 12 giờ/tài khoản không Active).
    /// </summary>
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct);

    /// <summary>Đăng xuất: thu hồi family hiện tại, hoặc mọi phiên của user nếu revokeAllDevices. Luôn thành công.</summary>
    Task LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct);

    /// <summary>Người đang đăng nhập. Lỗi: 401 SESSION_EXPIRED.</summary>
    Task<UserResponse> GetCurrentUserAsync(CancellationToken ct);
}
