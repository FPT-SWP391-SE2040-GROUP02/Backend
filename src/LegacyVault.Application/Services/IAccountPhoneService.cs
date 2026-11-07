using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Số điện thoại của người đang đăng nhập (mọi vai trò).</summary>
public interface IAccountPhoneService
{
    /// <summary>
    /// Đặt hoặc đổi số điện thoại. Số phải là di động Việt Nam (PhoneRules). Trả thông tin người dùng mới.
    /// Lỗi: 400 VALIDATION_ERROR (sai định dạng), 401 SESSION_EXPIRED.
    /// </summary>
    Task<UserResponse> SetMyPhoneAsync(SetPhoneRequest request, CancellationToken ct);
}
