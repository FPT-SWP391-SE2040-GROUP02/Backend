using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Điểm danh định kỳ của Owner đang đăng nhập (SRS §6.1 F2A, DMS-01..06).
/// </summary>
/// <remarks>
/// Lỗi chung: 401 SESSION_EXPIRED; 404 VAULT_NOT_FOUND khi chưa có kho.
/// Điểm danh chỉ để phát hiện việc cần kiểm tra, không chứng minh Owner còn sống hay đã mất (DMS-03);
/// đăng nhập hay thanh toán không phải điểm danh (DMS-02).
/// </remarks>
public interface ICheckInService
{
    /// <summary>Trạng thái, các hạn, cấu hình và một trang lịch sử. Kế hoạch chưa bắt đầu → Status NOT_STARTED.</summary>
    Task<CheckInStatusResponse> GetMyCheckInAsync(PaginationRequest pagination, CancellationToken ct);

    /// <summary>
    /// "Điểm danh": đóng kỳ hiện tại, mở kỳ mới (áp dụng cấu hình chờ), đưa kế hoạch về ACTIVE — một giao dịch.
    /// Bấm lặp trong 60 giây thì trả trạng thái hiện tại, không mở kỳ mới.
    /// Lỗi: 409 CHECKIN_NOT_ALLOWED (kế hoạch chưa kích hoạt hoặc đã sang giai đoạn chứng tử/bàn giao),
    /// 409 EDIT_CONFLICT (hai yêu cầu điểm danh song song).
    /// </summary>
    Task<CheckInStatusResponse> CheckInAsync(CancellationToken ct);

    /// <summary>
    /// Đổi chu kỳ/thời gian chờ (DMS-01). Có kỳ đang mở thì chỉ áp dụng từ kỳ sau.
    /// Lỗi: 400 VALIDATION_ERROR, 409 EDIT_CONFLICT (etag cũ), 409 VAULT_NOT_EDITABLE (PLAN_LOCKED / VAULT_CLOSED).
    /// </summary>
    Task<CheckInStatusResponse> UpdateSettingsAsync(UpdateCheckInSettingsRequest request, CancellationToken ct);
}
