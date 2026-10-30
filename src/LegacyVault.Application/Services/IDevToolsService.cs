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
    /// Đưa tài khoản về lúc vừa đăng ký và đăng nhập được: xóa toàn bộ dữ liệu eKYC (lượt xác minh, đồng ý chụp ảnh,
    /// danh tính đã gắn, mẫu khuôn mặt) và xóa hẳn kho của tài khoản cùng mọi dữ liệu trong kho (tài sản, gói, người nhận,
    /// Executor, kế hoạch, điểm danh, hồ sơ chứng tử, báo còn sống, bàn giao, thanh toán) và các tệp mã hóa liên quan.
    /// Email, mật khẩu, vai trò và vai trò Executor ở kho người khác giữ nguyên. Không có gì để xóa thì không ghi nhật ký.
    /// Lỗi: 400 VALIDATION_ERROR, 404 USER_NOT_FOUND (người gọi không phải Admin hoặc không có tài khoản với email này).
    /// </summary>
    Task<DevEkycResetResponse> ResetEkycAsync(DevEkycResetRequest request, CancellationToken ct);

    /// <summary>
    /// Đưa kho về thời điểm ngay trước bước eKYC và mua gói: xóa dữ liệu eKYC của Owner, trả quyền dịch vụ về Free,
    /// hủy đơn thanh toán đang chờ. Đơn đã trả, tài sản, người nhận, gói và Executor giữ nguyên. Kho vốn đã ở thời điểm đó thì không ghi gì.
    /// Lỗi: 404 USER_NOT_FOUND (không phải Admin hoặc không thấy Owner), 404 VAULT_NOT_FOUND,
    /// 409 DEV_TOOL_NOT_ALLOWED (kế hoạch không còn ở Nháp).
    /// </summary>
    Task<DevRewindBeforeEkycResponse> RewindBeforeEkycAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Đọc ngưỡng so khớp khuôn mặt eKYC đang chạy (gồm cả override runtime nếu DevTools đã đặt).
    /// Lỗi: 404 USER_NOT_FOUND (không phải Admin).
    /// </summary>
    Task<DevEkycThresholdResponse> GetEkycThresholdAsync(CancellationToken ct);

    /// <summary>
    /// Đặt lại ngưỡng so khớp khuôn mặt eKYC (override runtime, chỉ ở bộ nhớ, không lưu DB). Giá trị phải nằm trong
    /// dải an toàn [0.20; 0.60]; vi phạm trả 400. Ghi nhật ký kiểm toán (DevUpdateEkycThreshold).
    /// Lỗi: 400 VALIDATION_ERROR (ngoài dải an toàn), 404 USER_NOT_FOUND (không phải Admin).
    /// </summary>
    Task<DevEkycThresholdResponse> UpdateEkycThresholdAsync(DevEkycThresholdRequest request, CancellationToken ct);
}
