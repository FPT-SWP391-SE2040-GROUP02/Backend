using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Quản lý phương thức đăng nhập của người đang đăng nhập (AUTH-03): mật khẩu app và Google.
/// </summary>
/// <remarks>
/// - Mọi thao tác thêm/bỏ phải xác thực lại; tài khoản luôn còn ít nhất một phương thức.
/// - Người dùng lấy từ ICurrentUser, không nhận userId từ FE.
/// - Không đổi User.Email theo email Google (hai email độc lập, AUTH-02).
/// Lỗi chung: 401 SESSION_EXPIRED khi phiên không hợp lệ.
/// </remarks>
public interface ILoginMethodService
{
    /// <summary>Danh sách phương thức hiện có.</summary>
    Task<List<LoginMethodResponse>> GetMyLoginMethodsAsync(CancellationToken ct);

    /// <summary>
    /// Thêm Google vào tài khoản. Lỗi: 401 REAUTHENTICATION_FAILED (sai mật khẩu hiện tại),
    /// 401 INVALID_GOOGLE_TOKEN, 409 GOOGLE_ACCOUNT_ALREADY_LINKED (sub đã thuộc người khác / đã có liên kết Google).
    /// </summary>
    Task<LoginMethodResponse> LinkGoogleAsync(LinkGoogleRequest request, CancellationToken ct);

    /// <summary>
    /// Bỏ liên kết Google. Chưa liên kết → thành công luôn (idempotent).
    /// Lỗi: 409 LAST_LOGIN_METHOD (chưa có mật khẩu app), 401 REAUTHENTICATION_FAILED.
    /// </summary>
    Task UnlinkGoogleAsync(UnlinkGoogleRequest request, CancellationToken ct);

    /// <summary>
    /// Tài khoản chỉ có Google đặt thêm mật khẩu app. Google token mới phải có sub trùng liên kết hiện có.
    /// Lỗi: 409 LOGIN_METHOD_ALREADY_EXISTS, 401 INVALID_GOOGLE_TOKEN, 401 REAUTHENTICATION_FAILED.
    /// </summary>
    Task SetPasswordAsync(SetPasswordRequest request, CancellationToken ct);

    /// <summary>
    /// Bỏ mật khẩu app, chỉ còn Google.
    /// Lỗi: 409 LAST_LOGIN_METHOD (chưa liên kết Google), 401 REAUTHENTICATION_FAILED, 401 INVALID_GOOGLE_TOKEN.
    /// </summary>
    Task RemovePasswordAsync(RemovePasswordRequest request, CancellationToken ct);
}
