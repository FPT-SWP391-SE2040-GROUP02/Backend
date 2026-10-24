using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Công cụ thử nghiệm của Admin: tua thời gian của một kho thử để chạy hết luồng điểm danh, chứng tử và báo còn sống
/// mà không phải chờ ngày thật. Controller chỉ mở ở môi trường Development. Người gọi không phải Admin đang hoạt động
/// thì 404 như khu vực không tồn tại. Mọi lần tua và về thời gian hiện tại đều ghi nhật ký kiểm toán.
/// </summary>
public interface IDevToolsService
{
    /// <summary>Danh sách kho để chọn. Lỗi: 404 USER_NOT_FOUND khi không phải Admin.</summary>
    Task<List<DevVaultOptionResponse>> ListVaultsAsync(CancellationToken ct);

    /// <summary>Trạng thái kế hoạch, điểm danh, đồng hồ và các mốc tua được. Lỗi: 404 USER_NOT_FOUND, 404 VAULT_NOT_FOUND.</summary>
    Task<DevClockResponse> GetClockAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Tua tới một mốc: dời hạn tương ứng về quá khứ, rồi cho job điểm danh xử lý ngay với hai mốc điểm danh.
    /// Lỗi: 400 VALIDATION_ERROR (mốc không hợp lệ), 404 USER_NOT_FOUND, 404 VAULT_NOT_FOUND,
    /// 409 DEV_TOOL_NOT_ALLOWED (trạng thái hiện tại không có mốc này hoặc đã qua mốc).
    /// </summary>
    Task<DevClockResponse> TimeTravelAsync(Guid vaultId, DevTimeTravelRequest request, CancellationToken ct);

    /// <summary>
    /// Đưa đồng hồ của kho về thời gian thực: chỉ ghi nhận trong nhật ký, giữ nguyên mọi trạng thái đã phát sinh khi tua.
    /// Lỗi: 404 USER_NOT_FOUND, 404 VAULT_NOT_FOUND.
    /// </summary>
    Task<DevClockResponse> ResetClockAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Xóa toàn bộ dữ liệu eKYC của một tài khoản (lượt xác minh, đồng ý chụp ảnh, danh tính đã gắn, mẫu khuôn mặt)
    /// để làm lại từ đầu; email, vai trò và quan hệ với kho giữ nguyên. Tài khoản chưa có eKYC thì không ghi gì.
    /// Lỗi: 400 VALIDATION_ERROR, 404 USER_NOT_FOUND (người gọi không phải Admin hoặc không có tài khoản với email này).
    /// </summary>
    Task<DevEkycResetResponse> ResetEkycAsync(DevEkycResetRequest request, CancellationToken ct);
}
