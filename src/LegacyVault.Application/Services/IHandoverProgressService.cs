using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Executor theo dõi tiến độ bàn giao từng tài sản cho từng người nhận. Chỉ đọc, không thay đổi dữ liệu.
/// </summary>
/// <remarks>
/// Executor chỉ thấy trạng thái, hạn, tên và loại tài sản, đã tải hay chưa;
/// không thấy nội dung tài sản, số CCCD đầy đủ hay thông tin liên hệ của người nhận.
/// </remarks>
public interface IHandoverProgressService
{
    /// <summary>
    /// Tiến độ bàn giao của hồ sơ chứng tử. Hồ sơ chưa được duyệt thì handoverStarted = false và không có lượt.
    /// Lỗi: 401 SESSION_EXPIRED; 404 DEATH_CASE_NOT_FOUND khi không có hồ sơ, người gọi là Owner
    /// hoặc không phải Executor đang hiệu lực của kho (cùng một lỗi để không lộ hồ sơ nào đang tồn tại).
    /// </summary>
    Task<HandoverProgressResponse> GetProgressAsync(Guid caseId, CancellationToken ct);
}
